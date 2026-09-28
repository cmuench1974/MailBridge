namespace MailBridge.App.Localization;

/// <summary>
/// App-wide UI strings for English and German. Set once at startup from
/// the Settings page choice (applied after restart); queried everywhere
/// from XAML via <c>{x:Bind L('key')}</c> and from view models via
/// <see cref="Get(string, object[])"/>.
/// </summary>
public static class Strings
{
    public const string EnglishCode = "en";
    public const string GermanCode = "de";

    private static IReadOnlyDictionary<string, string>? _current;

    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["nav.accounts"] = "Accounts",
        ["nav.backup"] = "Backup",
        ["nav.restore"] = "Restore",
        ["nav.schedule"] = "Schedule",
        ["nav.settings"] = "Settings",

        ["common.cancel"] = "Cancel",
        ["common.browse"] = "Browse...",
        ["common.compress"] = "Compress into a single .zip file",
        ["common.selectFirst"] = "Select an account and destination folder first.",
        ["common.noPassword"] = "No stored password for this account - re-add it with a password.",

        ["accounts.title"] = "Accounts",
        ["accounts.intro"] = "Add an IMAP account (username + app password). Gmail/Outlook require OAuth2 and are not yet supported.",
        ["accounts.displayName"] = "Display name",
        ["accounts.host"] = "IMAP host",
        ["accounts.port"] = "Port",
        ["accounts.username"] = "Username",
        ["accounts.password"] = "Password / app password",
        ["accounts.add"] = "Add account",
        ["accounts.saveChanges"] = "Save changes",
        ["accounts.testConnection"] = "Test connection",
        ["accounts.keepPasswordHint"] = "Leave the password empty to keep the stored password.",
        ["accounts.saved"] = "Saved accounts",
        ["accounts.edit"] = "Edit",
        ["accounts.remove"] = "Remove",
        ["accounts.removeTitle"] = "Remove account?",
        ["accounts.removeBody"] = "Remove '{0}' ({1} on {2})? Its stored password will be deleted and any scheduled backup for it will no longer run.",
        ["accounts.enterHostUser"] = "Enter IMAP host and username first.",
        ["accounts.noPasswordTyped"] = "No password available: type the (app) password to test a new account.",
        ["accounts.connecting"] = "Connecting to {0}:{1}...",
        ["accounts.connOk"] = "Connection OK - authenticated as {0}, {1} folder(s) found.",
        ["accounts.connFailed"] = "Connection failed: {0}",

        ["backup.title"] = "Backup",
        ["backup.intro"] = "Downloads every folder and message from the selected account.",
        ["backup.sourceAccount"] = "Source account",
        ["backup.destination"] = "Destination folder",
        ["backup.start"] = "Start backup",
        ["backup.connecting"] = "Connecting...",
        ["backup.progress"] = "{0}: {1}/{2}",
        ["backup.completeZip"] = "Backup complete: {0} messages -> {1}",
        ["backup.completeDir"] = "Backup complete: {0} messages in {1}",
        ["backup.failed"] = "Backup failed: {0}",

        ["restore.ready"] = "Ready.",
        ["restore.title"] = "Restore",
        ["restore.intro"] = "Uploads messages from a backup into the target account. Existing messages that match on Message-ID and other attributes are detected and never duplicated blindly.",
        ["restore.path"] = "Backup folder or .zip file",
        ["restore.target"] = "Target account",
        ["restore.filters"] = "Filters (optional)",
        ["restore.from"] = "From contains",
        ["restore.subject"] = "Subject contains",
        ["restore.dateFrom"] = "From date",
        ["restore.dateTo"] = "To date",
        ["restore.start"] = "Start restore",
        ["restore.selectFirst"] = "Select a backup and a target account first.",
        ["restore.preparing"] = "Preparing backup...",
        ["restore.restoring"] = "Restoring...",
        ["restore.aborted"] = "Aborted. Restored: {0}, Replaced: {1}, Skipped: {2}",
        ["restore.done"] = "Done. Restored: {0}, Replaced: {1}, Skipped: {2}",
        ["restore.failed"] = "Restore failed: {0}",

        ["schedule.title"] = "Scheduled backups",
        ["schedule.intro"] = "Automatically back up an account on a recurring schedule using Windows Task Scheduler, even when MailBridge isn't open.",
        ["schedule.account"] = "Account",
        ["schedule.destination"] = "Destination folder",
        ["schedule.frequency"] = "Frequency",
        ["schedule.dayOfWeek"] = "Day of week (weekly only)",
        ["schedule.time"] = "Time of day",
        ["schedule.add"] = "Add schedule",
        ["schedule.existing"] = "Existing schedules",
        ["schedule.created"] = "Schedule created for {0}.",
        ["schedule.createFailed"] = "Could not create schedule: {0}",
        ["schedule.enabledMsg"] = "Schedule enabled.",
        ["schedule.disabledMsg"] = "Schedule disabled.",
        ["schedule.updateFailed"] = "Could not update schedule: {0}",
        ["schedule.removed"] = "Schedule removed.",

        ["settings.title"] = "Settings",
        ["settings.intro"] = "Defaults for new backups, the app appearance and the UI language. Saved values pre-fill the Backup and Schedule pages; the theme applies immediately.",
        ["settings.language"] = "Language",
        ["settings.languageNote"] = "Applied after restarting MailBridge.",
        ["settings.theme"] = "Theme",
        ["settings.defaultDir"] = "Default backup destination folder",
        ["settings.defaultZip"] = "Compress backups into a single .zip file by default",
        ["settings.save"] = "Save settings",
        ["settings.openData"] = "Open data folder",
        ["settings.saved"] = "Settings saved.",
    };

    private static readonly IReadOnlyDictionary<string, string> German = new Dictionary<string, string>
    {
        ["nav.accounts"] = "Konten",
        ["nav.backup"] = "Backup",
        ["nav.restore"] = "Wiederherstellen",
        ["nav.schedule"] = "Zeitplan",
        ["nav.settings"] = "Einstellungen",

        ["common.cancel"] = "Abbrechen",
        ["common.browse"] = "Auswählen...",
        ["common.compress"] = "In eine einzelne .zip-Datei komprimieren",
        ["common.selectFirst"] = "Bitte zuerst ein Konto und einen Zielordner wählen.",
        ["common.noPassword"] = "Kein gespeichertes Passwort für dieses Konto - bitte mit Passwort neu anlegen.",

        ["accounts.title"] = "Konten",
        ["accounts.intro"] = "IMAP-Konto hinzufügen (Benutzername + App-Passwort). Gmail/Outlook erfordern OAuth2 und werden noch nicht unterstützt.",
        ["accounts.displayName"] = "Anzeigename",
        ["accounts.host"] = "IMAP-Server",
        ["accounts.port"] = "Port",
        ["accounts.username"] = "Benutzername",
        ["accounts.password"] = "Passwort / App-Passwort",
        ["accounts.add"] = "Konto hinzufügen",
        ["accounts.saveChanges"] = "Änderungen speichern",
        ["accounts.testConnection"] = "Verbindung testen",
        ["accounts.keepPasswordHint"] = "Lassen Sie das Passwort leer, um das gespeicherte Passwort zu behalten.",
        ["accounts.saved"] = "Gespeicherte Konten",
        ["accounts.edit"] = "Bearbeiten",
        ["accounts.remove"] = "Entfernen",
        ["accounts.removeTitle"] = "Konto entfernen?",
        ["accounts.removeBody"] = "'{0}' ({1} auf {2}) entfernen? Das gespeicherte Passwort wird gelöscht und geplante Backups dafür laufen nicht mehr.",
        ["accounts.enterHostUser"] = "Bitte zuerst IMAP-Server und Benutzernamen eingeben.",
        ["accounts.noPasswordTyped"] = "Kein Passwort verfügbar: Geben Sie das (App-)Passwort ein, um ein neues Konto zu testen.",
        ["accounts.connecting"] = "Verbinde mit {0}:{1}...",
        ["accounts.connOk"] = "Verbindung OK - authentifiziert als {0}, {1} Ordner gefunden.",
        ["accounts.connFailed"] = "Verbindung fehlgeschlagen: {0}",

        ["backup.title"] = "Backup",
        ["backup.intro"] = "Lädt alle Ordner und Nachrichten des ausgewählten Kontos herunter.",
        ["backup.sourceAccount"] = "Quellkonto",
        ["backup.destination"] = "Zielordner",
        ["backup.start"] = "Backup starten",
        ["backup.connecting"] = "Verbinde...",
        ["backup.progress"] = "{0}: {1}/{2}",
        ["backup.completeZip"] = "Backup abgeschlossen: {0} Nachrichten -> {1}",
        ["backup.completeDir"] = "Backup abgeschlossen: {0} Nachrichten in {1}",
        ["backup.failed"] = "Backup fehlgeschlagen: {0}",

        ["restore.ready"] = "Bereit.",
        ["restore.title"] = "Wiederherstellen",
        ["restore.intro"] = "Überträgt Nachrichten aus einem Backup in das Zielkonto. Vorhandene Nachrichten, die per Message-ID und weiteren Attributen erkannt werden, gehen nicht verloren und werden nicht blind dupliziert.",
        ["restore.path"] = "Backup-Ordner oder .zip-Datei",
        ["restore.target"] = "Zielkonto",
        ["restore.filters"] = "Filter (optional)",
        ["restore.from"] = "Absender enthält",
        ["restore.subject"] = "Betreff enthält",
        ["restore.dateFrom"] = "Von",
        ["restore.dateTo"] = "Bis",
        ["restore.start"] = "Wiederherstellung starten",
        ["restore.selectFirst"] = "Bitte zuerst ein Backup und ein Zielkonto wählen.",
        ["restore.preparing"] = "Backup wird vorbereitet...",
        ["restore.restoring"] = "Wiederherstellung läuft...",
        ["restore.aborted"] = "Abgebrochen. Wiederhergestellt: {0}, Ersetzt: {1}, Übersprungen: {2}",
        ["restore.done"] = "Fertig. Wiederhergestellt: {0}, Ersetzt: {1}, Übersprungen: {2}",
        ["restore.failed"] = "Wiederherstellung fehlgeschlagen: {0}",

        ["schedule.title"] = "Geplante Backups",
        ["schedule.intro"] = "Sichert ein Konto automatisch nach Zeitplan über die Windows-Aufgabenplanung, auch wenn MailBridge nicht geöffnet ist.",
        ["schedule.account"] = "Konto",
        ["schedule.destination"] = "Zielordner",
        ["schedule.frequency"] = "Häufigkeit",
        ["schedule.dayOfWeek"] = "Wochentag (nur wöchentlich)",
        ["schedule.time"] = "Uhrzeit",
        ["schedule.add"] = "Zeitplan hinzufügen",
        ["schedule.existing"] = "Vorhandene Zeitpläne",
        ["schedule.created"] = "Zeitplan für {0} erstellt.",
        ["schedule.createFailed"] = "Zeitplan konnte nicht erstellt werden: {0}",
        ["schedule.enabledMsg"] = "Zeitplan aktiviert.",
        ["schedule.disabledMsg"] = "Zeitplan deaktiviert.",
        ["schedule.updateFailed"] = "Zeitplan konnte nicht aktualisiert werden: {0}",
        ["schedule.removed"] = "Zeitplan entfernt.",

        ["settings.title"] = "Einstellungen",
        ["settings.intro"] = "Standards für neue Backups, das Erscheinungsbild und die Sprache der Oberfläche. Gespeicherte Werte übernehmen die Backup- und Zeitplan-Seiten; das Erscheinungsbild greift sofort.",
        ["settings.language"] = "Sprache",
        ["settings.languageNote"] = "Wird nach dem Neustart von MailBridge angewendet.",
        ["settings.theme"] = "Erscheinungsbild",
        ["settings.defaultDir"] = "Standard-Zielordner für Backups",
        ["settings.defaultZip"] = "Backups standardmäßig in eine einzelne .zip-Datei komprimieren",
        ["settings.save"] = "Einstellungen speichern",
        ["settings.openData"] = "Datenordner öffnen",
        ["settings.saved"] = "Einstellungen gespeichert.",
    };

    public static IReadOnlyList<string> LanguageCodes { get; } = new[] { EnglishCode, GermanCode };

    public static string LanguageDisplayName(string code) => code == GermanCode ? "Deutsch" : "English";

    public static string DisplayNameToCode(string displayName) => displayName == "Deutsch" ? GermanCode : EnglishCode;

    public static void SetLanguage(string code)
    {
        _current = code == GermanCode ? German : English;
    }

    public static string Get(string key, params object[] args)
    {
        var current = _current ?? English;
        var text = current.TryGetValue(key, out var value) ? value
            : English.TryGetValue(key, out var fallback) ? fallback
            : key;

        return args.Length > 0 ? string.Format(text, args) : text;
    }
}
