// GameCha// GameChatHooks - Teil des ShibaBridge-Projekts.
// Aufgabe:
//  - Low-Level Hooks in den FFXIV-Chatmechanismus (via Signatures und Hooks).
//  - Ermöglicht Channel Overrides, Custom Channel Handler und /ss Commands.
//  - Erkennt, ob Nachrichten Befehle oder Chat-Text sind (inkl. Auto-Translate).
//  - Kann temporäre Channel/Tell-Overrides und UI-Anpassungen steuern.
//  - Nutzt das Dalamud Hooking-System, um Originalfunktionen abzufangen und zu erweitern.
//
// Hinweis: Viele Funktionen stammen aus FFXIV Reverse Engineering (u. a. ExtraChat-Projekt).
//          Unsicheres Arbeiten mit Pointern und Unmanaged Memory.

using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Component.Shell;
using ShibaBridge.Services;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.Interop;

public record ChatChannelOverride
{
    public string ChannelName = string.Empty;   // Name, der im Chat angezeigt wird
    public Action<byte[]>? ChatMessageHandler;  // Callback für Nachrichten in diesem Channel
}

public unsafe sealed class GameChatHooks : IDisposable
{
    // Based on https://git.anna.lgbt/anna/ExtraChat/src/branch/main/client/ExtraChat/GameFunctions.cs

    private readonly ILogger<GameChatHooks> _logger;
    private readonly Action<int, byte[]> _ssCommandHandler; // Handler für /ss1 ... /ssN Commands

    // Unmanaged Delegates, gebunden an Funktionen im Spiel-Client
    // Diese werden über Signatures (Pattern-Scanning) aufgelöst und als Hook-Targets genutzt.
    #region signatures
#pragma warning disable CS0649
    // Verarbeitungsschritt 2 für Strings (inkl. Pronouns & Auto-Translate)
    // Client::UI::Misc::PronounModule::???
    [Signature("E8 ?? ?? ?? ?? 44 88 74 24 ?? 4C 8D 45")]
    private readonly delegate* unmanaged<PronounModule*, Utf8String*, byte, Utf8String*> _processStringStep2;

    // Ausführen eines Chat-Befehls
    // Component::Shell::ShellCommandModule::ExecuteCommandInner
    private delegate void SendMessageDelegate(ShellCommandModule* module, Utf8String* message, UIModule* uiModule);
    [Signature("E8 ?? ?? ?? ?? FE 87 ?? ?? ?? ?? C7 87", DetourName = nameof(SendMessageDetour))]
    private Hook<SendMessageDelegate>? SendMessageHook { get; init; }

    // Setzen des Chat-Channels
    // Client::UI::Shell::RaptureShellModule::SetChatChannel
    private delegate void SetChatChannelDelegate(RaptureShellModule* module, uint channel);
    [Signature("E8 ?? ?? ?? ?? 33 C0 EB ?? 85 D2", DetourName = nameof(SetChatChannelDetour))]
    private Hook<SetChatChannelDelegate>? SetChatChannelHook { get; init; }

    // Änderung des Channel-Namens (UI)
    // Component::Shell::ShellCommandModule::ChangeChannelName
    private delegate byte* ChangeChannelNameDelegate(AgentChatLog* agent);
    [Signature("E8 ?? ?? ?? ?? BA ?? ?? ?? ?? 48 8D 4D B0 48 8B F8 E8 ?? ?? ?? ?? 41 8B D6", DetourName = nameof(ChangeChannelNameDetour))]
    private Hook<ChangeChannelNameDelegate>? ChangeChannelNameHook { get; init; }

    // Steuerung, ob Channel-Name Lookup ausgeführt werden soll
    // Client::UI::Agent::AgentChatLog::???
    private delegate byte ShouldDoNameLookupDelegate(AgentChatLog* agent);
    [Signature("48 89 5C 24 ?? 57 48 83 EC ?? 48 8B D9 40 32 FF 48 8B 49 ?? ?? ?? ?? FF 50", DetourName = nameof(ShouldDoNameLookupDetour))]
    private Hook<ShouldDoNameLookupDelegate>? ShouldDoNameLookupHook { get; init; }

    // Temporäre Channeländerung (Hotkey)
    // Client::UI::Shell::RaptureShellModule::???
    private delegate ulong TempChatChannelDelegate(RaptureShellModule* module, uint x, uint y, ulong z);
    [Signature("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 83 B9 ?? ?? ?? ?? ?? 49 8B F9 41 8B F0", DetourName = nameof(TempChatChannelDetour))]
    private Hook<TempChatChannelDelegate>? TempChatChannelHook { get; init; }

    // Temporäres Tell-Target setzen (Hotkey)
    // Client::UI::Shell::RaptureShellModule::SetContextTellTargetInForay
    private delegate ulong TempTellTargetDelegate(RaptureShellModule* module, ulong a, ulong b, ulong c, ushort d, ulong e, ulong f, ushort g);
    [Signature("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 83 B9 ?? ?? ?? ?? ?? 41 0F B7 F9", DetourName = nameof(TempTellTargetDetour))]
    private Hook<TempTellTargetDelegate>? TempTellTargetHook { get; init; }

    // Wird jedes Frame aufgerufen, wenn Chatbar nicht fokussiert ist
    private delegate void UnfocusTickDelegate(RaptureShellModule* module);
    [Signature("40 53 48 83 EC ?? 83 B9 ?? ?? ?? ?? ?? 48 8B D9 0F 84 ?? ?? ?? ?? 48 8D 91", DetourName = nameof(UnfocusTickDetour))]
    private Hook<UnfocusTickDelegate>? UnfocusTickHook { get; init; }
#pragma warning restore CS0649
    #endregion

    // Aktueller Chat-Channel Override (null = kein Override aktiv)
    private ChatChannelOverride? _chatChannelOverride;
    private ChatChannelOverride? _chatChannelOverrideTempBuffer;
    private bool _shouldForceNameLookup = false;

    // Zeitlimit für /r Reply-Erkennung
    private DateTime _nextMessageIsReply = DateTime.UnixEpoch;

    // Getter/Setter für den Chat-Channel Override
    public ChatChannelOverride? ChatChannelOverride
    {
        get => _chatChannelOverride;
        set {
            _chatChannelOverride = value;
            _shouldForceNameLookup = true;
        }
    }

    // Temporäres Zwischenspeichern des Chat-Channel Overrides
    private void StashChatChannel()
    {
        // Wenn ein temporärer Channel gesetzt wird, den aktuellen Override sichern und deaktivieren
        if (_chatChannelOverride != null)
        {
            _logger.LogTrace("Stashing chat channel");
            _chatChannelOverrideTempBuffer = _chatChannelOverride;
            ChatChannelOverride = null;
        }
    }

    // Wiederherstellen des Chat-Channel Overrides aus dem Zwischenspeicher
    private void UnstashChatChannel()
    {
        // Wenn ein temporärer Channel deaktiviert wird, den gesicherten Override wiederherstellen
        if (_chatChannelOverrideTempBuffer != null)
        {
            _logger.LogTrace("Unstashing chat channel");
            ChatChannelOverride = _chatChannelOverrideTempBuffer;
            _chatChannelOverrideTempBuffer = null;
        }
    }

    // Konstruktor - Initialisiert die Hooks und aktiviert sie
    public GameChatHooks(ILogger<GameChatHooks> logger, IGameInteropProvider gameInteropProvider, Action<int, byte[]> ssCommandHandler)
    {
        // Speichere die Abhängigkeiten
        _logger = logger;
        _ssCommandHandler = ssCommandHandler;

        // Initialisiere Hooks via Attribute
        logger.LogInformation("Initializing GameChatHooks");
        gameInteropProvider.InitializeFromAttributes(this);

        // Aktiviere alle Hooks
        SendMessageHook?.Enable();
        SetChatChannelHook?.Enable();
        ChangeChannelNameHook?.Enable();
        ShouldDoNameLookupHook?.Enable();
        TempChatChannelHook?.Enable();
        TempTellTargetHook?.Enable();
        UnfocusTickHook?.Enable();
    }

    // Dispose - Deaktiviert und entfernt alle Hooks
    public void Dispose()
    {
        SendMessageHook?.Dispose();
        SetChatChannelHook?.Dispose();
        ChangeChannelNameHook?.Dispose();
        ShouldDoNameLookupHook?.Dispose();
        TempChatChannelHook?.Dispose();
        TempTellTargetHook?.Dispose();
        UnfocusTickHook?.Dispose();
    }

    // Verarbeitet eine Chat-Nachricht (Pronouns, Auto-Translate, etc.) und gibt die resultierenden Bytes zurück
    private byte[] ProcessChatMessage(Utf8String* message)
    {
        // Hole das PronounModule für die String-Verarbeitung
        var pronounModule = UIModule.Instance()->GetPronounModule();

        // Chat-Nachricht in zwei Schritten verarbeiten, wie es der Client auch tut
        var chatString1 = pronounModule->ProcessString(message, true);
        var chatString2 = _processStringStep2(pronounModule, chatString1, 1);

        // Lese die resultierenden Bytes aus dem finalen Utf8String
        return MemoryHelper.ReadRaw((nint)chatString2->StringPtr.Value, chatString2->Length);
    }

    // Detour für das Senden von Nachrichten - erkennt Befehle, verarbeitet /ss und leitet Chat-Nachrichten weiter
    private void SendMessageDetour(ShellCommandModule* thisPtr, Utf8String* message, UIModule* uiModule)
    {
        try
        {
            // Analysiere die Nachricht, um zu bestimmen, ob es sich um einen Befehl handelt
            var messageLength = message->Length;
            var messageSpan = message->AsSpan();

            // Flags für Befehls- und Reply-Erkennung
            bool isCommand = false;
            bool isReply = false;

            // Aktuelle Zeit für Reply-Zeitlimit
            var utcNow = DateTime.UtcNow;

            // Prüfe auf /r Reply (zeitbasiert) oder führende / (Befehl)
            if (_nextMessageIsReply >= utcNow)
            {
                isCommand = true;
            }
            // Leere Nachricht, oder beginnt mit /, oder enthält nur Leerzeichen
            else if (messageLength == 0 || messageSpan[0] == (byte)'/' || !messageSpan.ContainsAnyExcept((byte)' '))
            {
                isCommand = true;

                // Prüfe auf /r oder /reply
                if (messageSpan.StartsWith(System.Text.Encoding.ASCII.GetBytes("/r ")) || messageSpan.StartsWith(System.Text.Encoding.ASCII.GetBytes("/reply ")))
                    isReply = true;
            }
            // Prüfe auf Auto-Translate Payload (Payload.START_BYTE = 0x02)
            else if (messageSpan[0] == (byte)0x02) 
            {
                // Versuche, die Payload zu dekodieren und prüfe, ob es sich um Auto-Translate handelt
                var payload = Payload.Decode(new BinaryReader(new UnmanagedMemoryStream(message->StringPtr, message->BufSize))) as AutoTranslatePayload;

                // Wenn es eine Auto-Translate Payload ist, prüfe auf führende /
                if (payload != null && payload.Text.Length > 2 && payload.Text[2] == '/')
                {
                    isCommand = true;

                    // Prüfe auf /r oder /reply nach dem Payload-Präfix
                    if (payload.Text[2..].StartsWith("/r ", StringComparison.Ordinal) || payload.Text[2..].StartsWith("/reply ", StringComparison.Ordinal))
                        isReply = true;
                }
            }

            // Setze das Reply-Zeitlimit, wenn es sich um eine Reply handelt
            // Dies erlaubt es, unmittelbar nach einem /r eine weitere Nachricht als Reply zu senden
            if (isReply)
                _nextMessageIsReply = utcNow + TimeSpan.FromMilliseconds(100);

            // Prüfe auf /ssN Commands (nur wenn es ein Befehl ist)
            // Diese werden immer an den Command Handler weitergeleitet und nicht normal verarbeitet
            if (isCommand && messageSpan.StartsWith(System.Text.Encoding.ASCII.GetBytes("/ss")))
            {
                // Prüfe auf /ss1 bis /ssN
                for (int i = 1; i <= ChatService.CommandMaxNumber; ++i)
                {
                    // Baue den zu prüfenden Command-String
                    var cmdString = $"/ss{i} ";

                    // Prüfe, ob die Nachricht mit diesem Command-String beginnt
                    if (messageSpan.StartsWith(System.Text.Encoding.ASCII.GetBytes(cmdString)))
                    {
                        // Verarbeite die Chat-Nachricht, um die Bytes zu erhalten
                        var ssChatBytes = ProcessChatMessage(message);
                        ssChatBytes = ssChatBytes.Skip(cmdString.Length).ToArray();

                        // Rufe den Command Handler mit der Shell-Nummer und den Chat-Bytes auf
                        _ssCommandHandler?.Invoke(i, ssChatBytes);
                        return;
                    }
                }
            }

            // Wenn es kein Befehl ist oder kein Chat-Channel Override aktiv ist, die Nachricht normal senden
            if (isCommand || _chatChannelOverride == null)
            {
                // Setze das Reply-Zeitlimit zurück, wenn es kein Reply war
                SendMessageHook!.OriginalDisposeSafe(thisPtr, message, uiModule);
                return;
            }

            // Verarbeite die Chat-Nachricht, um die Bytes zu erhalten
            // Diese werden an den Chat-Channel Handler weitergeleitet
            var chatBytes = ProcessChatMessage(message);

            // Rufe den Chat-Channel Handler mit den Chat-Bytes auf
            if (chatBytes.Length > 0)
                _chatChannelOverride.ChatMessageHandler?.Invoke(chatBytes);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception thrown during SendMessageDetour");
        }
    }

    // Detour für das Setzen des Chat-Channels - entfernt den Override, wenn der Channel geändert wird
    private void SetChatChannelDetour(RaptureShellModule* module, uint channel)
    {
        try
        {
            // Wenn der Chat-Channel geändert wird, den Override entfernen
            if (_chatChannelOverride != null)
            {
                // Logger nur auf Trace-Level, da dies häufig passiert
                _chatChannelOverride = null;
                _shouldForceNameLookup = true;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception thrown during SetChatChannelDetour");
        }

        // Rufe die Originalfunktion auf, um den Channel tatsächlich zu ändern
        SetChatChannelHook!.OriginalDisposeSafe(module, channel);
    }

    // Detour für temporäre Channeländerung (Hotkey) - sichert und entfernt den Override
    private ulong TempChatChannelDetour(RaptureShellModule* module, uint x, uint y, ulong z)
    {
        // Rufe die Originalfunktion auf, um den temporären Channel zu setzen
        var result = TempChatChannelHook!.OriginalDisposeSafe(module, x, y, z);

        // Wenn ein temporärer Channel gesetzt wurde, den aktuellen Override sichern und deaktivieren
        if (result != 0)
            StashChatChannel();

        return result;
    }

    // Detour für temporäres Tell-Target setzen (Hotkey) - sichert und entfernt den Override
    private ulong TempTellTargetDetour(RaptureShellModule* module, ulong a, ulong b, ulong c, ushort d, ulong e, ulong f, ushort g)
    {
        // Rufe die Originalfunktion auf, um das temporäre Tell-Target zu setzen
        var result = TempTellTargetHook!.OriginalDisposeSafe(module, a, b, c, d, e, f, g);

        // Wenn ein temporäres Tell-Target gesetzt wurde, den aktuellen Override sichern und deaktivieren
        if (result != 0)
            StashChatChannel();

        return result;
    }

    // Detour, die jedes Frame aufgerufen wird, wenn die Chatbar nicht fokussiert ist
    private void UnfocusTickDetour(RaptureShellModule* module)
    {
        // Rufe die Originalfunktion auf
        UnfocusTickHook!.OriginalDisposeSafe(module);
        UnstashChatChannel();
    }

    // Detour für das Ändern des Channel-Namens - ersetzt den Namen, wenn ein Override aktiv ist
    private byte* ChangeChannelNameDetour(AgentChatLog* agent)
    {
        // Rufe die Originalfunktion auf, um den Channel-Namen zu ändern
        var originalResult = ChangeChannelNameHook!.OriginalDisposeSafe(agent);

        try
        {
            // Replace the chat channel name on the UI if active
            if (_chatChannelOverride != null)
            {
                // Logger nur auf Trace-Level, da dies häufig passiert
                agent->ChannelLabel.SetString(_chatChannelOverride.ChannelName);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception thrown during ChangeChannelNameDetour");
        }

        return originalResult;
    }

    // Detour für die Steuerung, ob ein Channel-Name Lookup ausgeführt werden soll
    private byte ShouldDoNameLookupDetour(AgentChatLog* agent)
    {
        // Rufe die Originalfunktion auf, um zu bestimmen, ob ein Name Lookup durchgeführt werden soll
        var originalResult = ShouldDoNameLookupHook!.OriginalDisposeSafe(agent);

        try
        {
            // Erzwinge ein Name Lookup, wenn der Chat-Channel Override geändert wurde
            if (_shouldForceNameLookup)
            {
                _shouldForceNameLookup = false;
                return 1;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception thrown during ShouldDoNameLookupDetour");
        }

        return originalResult;
    }
}
