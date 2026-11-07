using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using MailKit.Net.Imap;
using MailKit.Security;

namespace MailBridge.Core.Tests.Imap;

/// <summary>A mailbox on <see cref="FakeImapServer"/>.</summary>
internal sealed record FakeFolder(string Name, bool Subscribed = true, bool NoSelect = false);

/// <summary>
/// Minimal IMAP server on the loopback interface, without TLS - just enough for MailKit's folder
/// handling (NAMESPACE, LIST, LSUB, CREATE, SUBSCRIBE) and for a backup and a restore run
/// (STATUS, SELECT/EXAMINE, UID SEARCH ALL, UID FETCH of bodies, flags and sizes, APPEND,
/// EXPUNGE, CLOSE/UNSELECT). INBOX always exists. Like real servers, CREATE leaves a new folder
/// unsubscribed. Messages must be ASCII: literals are counted in characters.
/// </summary>
internal sealed class FakeImapServer : IAsyncDisposable
{
    private const string Capabilities = "IMAP4rev1 NAMESPACE UNSELECT";

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<FakeFolder> _folders;
    private readonly Dictionary<string, List<FakeMessage>> _messages = new(StringComparer.Ordinal);
    private readonly char _separator;
    private readonly string _namespacePrefix;
    private readonly Task _acceptLoop;
    private uint _nextUid = 1;

    public FakeImapServer(char separator, string namespacePrefix, params FakeFolder[] folders)
    {
        _separator = separator;
        _namespacePrefix = namespacePrefix;
        _folders = folders.Any(f => IsInbox(f.Name)) ? folders.ToList() : folders.Prepend(new FakeFolder("INBOX")).ToList();
        _listener.Start();
        _acceptLoop = AcceptLoopAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Received commands without their tag, in order.</summary>
    public List<string> Commands { get; } = new();

    public IReadOnlyList<string> FolderNames
    {
        get
        {
            lock (_folders)
            {
                return _folders.Select(f => f.Name).ToList();
            }
        }
    }

    public IReadOnlyList<string> SubscribedFolderNames
    {
        get
        {
            lock (_folders)
            {
                return _folders.Where(f => f.Subscribed).Select(f => f.Name).ToList();
            }
        }
    }

    /// <summary>Puts a message (RFC 822 text with CRLF line breaks) into an existing folder.</summary>
    public void AddMessage(string folder, string raw, string flags = "")
    {
        lock (_folders)
        {
            MessagesOf(Normalize(folder)).Add(new FakeMessage(_nextUid++, raw, flags));
        }
    }

    /// <summary>The raw messages of a folder, e.g. as appended by a restore.</summary>
    public IReadOnlyList<string> MessagesIn(string folder)
    {
        lock (_folders)
        {
            return _messages.TryGetValue(Normalize(folder), out var messages) ? messages.Select(m => m.Raw).ToList() : new List<string>();
        }
    }

    public async Task<ImapClient> ConnectAsync()
    {
        var client = new ImapClient();
        await client.ConnectAsync("127.0.0.1", Port, SecureSocketOptions.None);
        await client.AuthenticateAsync("user", "secret");
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try
        {
            await _acceptLoop;
        }
        catch (Exception)
        {
            // stopping the listener ends the accept loop with an exception
        }

        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
            _ = ServeAsync(connection);
        }
    }

    private async Task ServeAsync(TcpClient connection)
    {
        using (connection)
        {
            var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            var session = new Session();

            await writer.WriteLineAsync($"* OK [CAPABILITY {Capabilities}] Fake IMAP ready");
            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                // APPEND sends the message as a literal: "{123}" ends the line, the data follows.
                string? literal = null;
                var literalMatch = Regex.Match(line, @"\{(\d+)(\+?)\}$");
                if (literalMatch.Success)
                {
                    if (literalMatch.Groups[2].Length == 0)
                    {
                        await writer.WriteLineAsync("+ Ready for literal data");
                    }

                    var buffer = new char[int.Parse(literalMatch.Groups[1].Value)];
                    await reader.ReadBlockAsync(buffer, _stop.Token);
                    literal = new string(buffer);
                    await reader.ReadLineAsync(_stop.Token); // the CRLF that ends the command
                    line = line[..literalMatch.Index].TrimEnd();
                }

                var space = line.IndexOf(' ');
                var tag = line[..space];
                var command = line[(space + 1)..];
                lock (Commands)
                {
                    Commands.Add(command);
                }

                var arguments = ParseArguments(command);
                var name = arguments[0].ToUpperInvariant();
                arguments.RemoveAt(0);
                if (name == "UID")
                {
                    name = "UID " + arguments[0].ToUpperInvariant();
                    arguments.RemoveAt(0);
                }

                var reply = Respond(name, arguments, command, literal, session);
                foreach (var response in reply.Untagged)
                {
                    await writer.WriteLineAsync(response);
                }

                await writer.WriteLineAsync($"{tag} {(reply.Ok ? "OK" : "NO")} {(reply.Code is null ? string.Empty : reply.Code + " ")}{name} {(reply.Ok ? "completed" : "failed")}");
                if (name == "LOGOUT")
                {
                    return;
                }
            }
        }
    }

    private Reply Respond(string command, List<string> arguments, string commandLine, string? literal, Session session)
    {
        lock (_folders)
        {
            switch (command)
            {
                case "CAPABILITY":
                    return new Reply($"* CAPABILITY {Capabilities}");
                case "NAMESPACE":
                    return new Reply($"* NAMESPACE ((\"{_namespacePrefix}\" \"{_separator}\")) NIL NIL");
                case "LIST":
                case "LSUB":
                    return new Reply(List(command, arguments[1]).ToArray());
                case "CREATE":
                    _folders.Add(new FakeFolder(arguments[0].TrimEnd(_separator), Subscribed: false));
                    return new Reply();
                case "SUBSCRIBE":
                    var index = _folders.FindIndex(f => f.Name == arguments[0]);
                    if (index >= 0)
                    {
                        _folders[index] = _folders[index] with { Subscribed = true };
                    }

                    return new Reply();
                case "STATUS":
                    return Exists(arguments[0])
                        ? new Reply($"* STATUS \"{arguments[0]}\" (MESSAGES {MessagesOf(Normalize(arguments[0])).Count})")
                        : new Reply { Ok = false };
                case "SELECT":
                case "EXAMINE":
                    if (!Exists(arguments[0]))
                    {
                        return new Reply { Ok = false };
                    }

                    session.Selected = Normalize(arguments[0]);
                    return new Reply(
                        "* FLAGS (\\Answered \\Flagged \\Deleted \\Seen \\Draft)",
                        $"* {MessagesOf(session.Selected).Count} EXISTS",
                        "* 0 RECENT",
                        "* OK [UIDVALIDITY 1] UIDs valid",
                        $"* OK [UIDNEXT {_nextUid}] Predicted next UID") { Code = command == "SELECT" ? "[READ-WRITE]" : "[READ-ONLY]" };
                case "UID SEARCH":
                    return new Reply("* SEARCH" + string.Concat(Selected(session).Select(m => " " + m.Uid)));
                case "UID FETCH":
                    return new Reply(Fetch(Selected(session), arguments[0], commandLine).ToArray());
                case "APPEND":
                    if (!Exists(arguments[0]) || literal is null)
                    {
                        return new Reply { Ok = false, Code = "[TRYCREATE]" };
                    }

                    var flags = arguments.Skip(1).FirstOrDefault(a => a.StartsWith('(')) ?? "()";
                    MessagesOf(Normalize(arguments[0])).Add(new FakeMessage(_nextUid++, literal, flags.Trim('(', ')')));
                    return new Reply();
                case "EXPUNGE":
                    return new Reply(Expunge(session).ToArray());
                case "CLOSE":
                    Expunge(session);
                    session.Selected = null;
                    return new Reply();
                case "UNSELECT":
                    session.Selected = null;
                    return new Reply();
                case "LOGOUT":
                    return new Reply("* BYE");
                default:
                    return new Reply();
            }
        }
    }

    private IEnumerable<string> Fetch(List<FakeMessage> messages, string set, string commandLine)
    {
        var max = messages.Count == 0 ? 0 : messages.Max(m => m.Uid);
        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            if (!InSet(message.Uid, set, max))
            {
                continue;
            }

            yield return commandLine.Contains("BODY.PEEK[]", StringComparison.OrdinalIgnoreCase)
                ? $"* {i + 1} FETCH (UID {message.Uid} BODY[] {{{message.Raw.Length}}}\r\n{message.Raw})"
                : $"* {i + 1} FETCH (UID {message.Uid} FLAGS ({message.Flags}) RFC822.SIZE {message.Raw.Length})";
        }
    }

    private IEnumerable<string> Expunge(Session session)
    {
        var messages = Selected(session);
        var responses = new List<string>();
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Flags.Contains("\\Deleted", StringComparison.OrdinalIgnoreCase))
            {
                messages.RemoveAt(i);
                responses.Add($"* {i + 1} EXPUNGE");
            }
        }

        return responses;
    }

    private static bool InSet(uint uid, string set, uint max) => set.Split(',').Any(part =>
    {
        var range = part.Split(':');
        var low = range[0] == "*" ? max : uint.Parse(range[0]);
        var high = range.Length < 2 ? low : range[1] == "*" ? max : uint.Parse(range[1]);
        return uid >= Math.Min(low, high) && uid <= Math.Max(low, high);
    });

    private List<FakeMessage> Selected(Session session) =>
        session.Selected is { } name ? MessagesOf(name) : new List<FakeMessage>();

    private List<FakeMessage> MessagesOf(string normalizedName)
    {
        if (!_messages.TryGetValue(normalizedName, out var messages))
        {
            _messages[normalizedName] = messages = new List<FakeMessage>();
        }

        return messages;
    }

    private bool Exists(string name) => _folders.Any(f => !f.NoSelect && Normalize(f.Name) == Normalize(name));

    private static string Normalize(string name) => IsInbox(name) ? "INBOX" : name;

    private IEnumerable<string> List(string command, string pattern)
    {
        if (pattern.Length == 0)
        {
            // LIST "" "" asks for the hierarchy separator only.
            return new[] { $"* LIST (\\Noselect) \"{_separator}\" \"\"" };
        }

        var regex = new Regex("^" + string.Concat(pattern.Select(c => c switch
        {
            '*' => ".*",
            '%' => $"[^{Regex.Escape(_separator.ToString())}]*",
            _ => Regex.Escape(c.ToString()),
        })) + "$", RegexOptions.CultureInvariant);

        return _folders
            .Where(f => command == "LIST" || f.Subscribed)
            .Where(f => regex.IsMatch(f.Name) || (IsInbox(f.Name) && IsInbox(pattern)))
            .Select(f =>
            {
                var hasChildren = _folders.Any(c => c.Name.StartsWith(f.Name + _separator, StringComparison.Ordinal));
                var attributes = (f.NoSelect ? "\\Noselect " : string.Empty) + (hasChildren ? "\\HasChildren" : "\\HasNoChildren");
                return $"* {command} ({attributes}) \"{_separator}\" \"{f.Name}\"";
            })
            .ToList();
    }

    private static bool IsInbox(string name) => string.Equals(name, "INBOX", StringComparison.OrdinalIgnoreCase);

    /// <summary>Splits a command line into atoms and quoted strings (literals are read separately).</summary>
    private static List<string> ParseArguments(string command)
    {
        var arguments = new List<string>();
        var i = 0;
        while (i < command.Length)
        {
            if (command[i] == ' ')
            {
                i++;
                continue;
            }

            var value = new StringBuilder();
            if (command[i] == '"')
            {
                for (i++; i < command.Length && command[i] != '"'; i++)
                {
                    value.Append(command[i] == '\\' ? command[++i] : command[i]);
                }

                i++;
            }
            else
            {
                for (; i < command.Length && command[i] != ' '; i++)
                {
                    value.Append(command[i]);
                }
            }

            arguments.Add(value.ToString());
        }

        return arguments;
    }

    private sealed record FakeMessage(uint Uid, string Raw, string Flags);

    /// <summary>Per-connection state.</summary>
    private sealed class Session
    {
        public string? Selected { get; set; }
    }

    private sealed class Reply
    {
        public Reply(params string[] untagged)
        {
            Untagged = untagged;
        }

        public IReadOnlyList<string> Untagged { get; }

        public bool Ok { get; init; } = true;

        /// <summary>Response code in the tagged reply, e.g. "[READ-WRITE]".</summary>
        public string? Code { get; init; }
    }
}
