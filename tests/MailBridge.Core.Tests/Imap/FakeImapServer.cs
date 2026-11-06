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
/// Minimal IMAP server on the loopback interface - just enough LOGIN, NAMESPACE, LIST, LSUB
/// and CREATE for MailKit's folder handling, over a mutable set of mailboxes. INBOX always exists.
/// </summary>
internal sealed class FakeImapServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<FakeFolder> _folders;
    private readonly char _separator;
    private readonly string _namespacePrefix;
    private readonly Task _acceptLoop;

    public FakeImapServer(char separator, string namespacePrefix, params FakeFolder[] folders)
    {
        _separator = separator;
        _namespacePrefix = namespacePrefix;
        _folders = folders.Any(f => IsInbox(f.Name)) ? folders.ToList() : folders.Prepend(new FakeFolder("INBOX")).ToList();
        _listener.Start();
        _acceptLoop = AcceptLoopAsync();
    }

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

    public async Task<ImapClient> ConnectAsync()
    {
        var client = new ImapClient();
        await client.ConnectAsync("127.0.0.1", ((IPEndPoint)_listener.LocalEndpoint).Port, SecureSocketOptions.None);
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

            await writer.WriteLineAsync("* OK [CAPABILITY IMAP4rev1 NAMESPACE] Fake IMAP ready");
            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                var space = line.IndexOf(' ');
                var tag = line[..space];
                var command = line[(space + 1)..];
                lock (Commands)
                {
                    Commands.Add(command);
                }

                var arguments = ParseArguments(command);
                var name = arguments[0].ToUpperInvariant();
                foreach (var response in Respond(name, arguments.Skip(1).ToList()))
                {
                    await writer.WriteLineAsync(response);
                }

                await writer.WriteLineAsync($"{tag} OK {name} completed");
                if (name == "LOGOUT")
                {
                    return;
                }
            }
        }
    }

    private IEnumerable<string> Respond(string command, List<string> arguments)
    {
        switch (command)
        {
            case "CAPABILITY":
                return new[] { "* CAPABILITY IMAP4rev1 NAMESPACE" };
            case "NAMESPACE":
                return new[] { $"* NAMESPACE ((\"{_namespacePrefix}\" \"{_separator}\")) NIL NIL" };
            case "LIST":
            case "LSUB":
                return List(command, arguments[1]);
            case "CREATE":
                lock (_folders)
                {
                    _folders.Add(new FakeFolder(arguments[0].TrimEnd(_separator), Subscribed: false));
                }

                return Array.Empty<string>();
            case "LOGOUT":
                return new[] { "* BYE" };
            default:
                return Array.Empty<string>();
        }
    }

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

        lock (_folders)
        {
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
    }

    private static bool IsInbox(string name) => string.Equals(name, "INBOX", StringComparison.OrdinalIgnoreCase);

    /// <summary>Splits a command line into atoms and quoted strings (no literals needed here).</summary>
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
}
