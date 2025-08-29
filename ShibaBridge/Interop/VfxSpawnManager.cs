// VfxSpawnManager - Teil des ShibaBridge-Projekts.
// Aufgabe:
//  - Direkte Interaktion mit dem FFXIV-Speicher via Signaturen (Dalamud GameInterop).
//  - Erzeugung, Bewegung, Sichtbarkeitssteuerung und Entfernen von VFX-Objekten.
//  - Integration mit dem Mediator-System, um VFX bei z. B. Cutscenes oder Gpose auszublenden.
// Hinweis: Unsichere Operationen (unsafe code) und Memory-Pointer werden direkt genutzt,
//          deshalb werden VFX-Strukturen 1:1 aus dem Spiel gelesen/manipuliert.

using Dalamud.Memory;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using ShibaBridge.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace ShibaBridge.Interop;

/// <summary>
/// Code für das Spawning basiert stark auf Anna’s OrangeGuidanceTomestone Projekt. https://git.anna.lgbt/anna/OrangeGuidanceTomestone/src/branch/main/client/Vfx.cs
/// </summary>
public unsafe class VfxSpawnManager : DisposableMediatorSubscriberBase
{
    // Name des internen Pools im Spiel, wird bei der Erstellung neuer VFX-Objekte benötigt
    private static readonly byte[] _pool = "Client.System.Scheduler.Instance.VfxObject\0"u8.ToArray();

    // Mit Signaturen werden Speicheradressen im Spiel identifiziert und als Delegates eingebunden.
    // Sie erlauben direkte Aufrufe von Spiel-internen Funktionen (Reverse Engineering).
    #region signatures
#pragma warning disable CS0649
    [Signature("E8 ?? ?? ?? ?? F3 0F 10 35 ?? ?? ?? ?? 48 89 43 08")]
    private readonly delegate* unmanaged<byte*, byte*, VfxStruct*> _staticVfxCreate;

    [Signature("E8 ?? ?? ?? ?? ?? ?? ?? 8B 4A ?? 85 C9")]
    private readonly delegate* unmanaged<VfxStruct*, float, int, ulong> _staticVfxRun;

    [Signature("40 53 48 83 EC 20 48 8B D9 48 8B 89 ?? ?? ?? ?? 48 85 C9 74 28 33 D2 E8 ?? ?? ?? ?? 48 8B 8B ?? ?? ?? ?? 48 85 C9")]
    private readonly delegate* unmanaged<VfxStruct*, nint> _staticVfxRemove;
    #pragma warning restore CS0649
    #endregion

    public VfxSpawnManager(ILogger<VfxSpawnManager> logger, IGameInteropProvider gameInteropProvider, ShibaBridgeMediator shibabridgeMediator)
        : base(logger, shibabridgeMediator)
    {
        // Initialisiert GameInterop: scannt Signaturen und bindet die Methoden
        gameInteropProvider.InitializeFromAttributes(this);

        // Abonniert Mediator-Nachrichten, um VFX bei Gpose oder Cutscenes auszublenden
        shibabridgeMediator.Subscribe<GposeStartMessage>(this, (msg) =>
        {
            ChangeSpawnVisibility(0f);
        });
        shibabridgeMediator.Subscribe<GposeEndMessage>(this, (msg) =>
        {
            RestoreSpawnVisiblity();
        });
        shibabridgeMediator.Subscribe<CutsceneStartMessage>(this, (msg) =>
        {
            ChangeSpawnVisibility(0f);
        });
        shibabridgeMediator.Subscribe<CutsceneEndMessage>(this, (msg) =>
        {
            RestoreSpawnVisiblity();
        });
    }

    // -----------------------
    // Sichtbarkeit
    // -----------------------

    // Stellt die ursprüngliche Sichtbarkeit aller VFX-Objekte wieder her
    private unsafe void RestoreSpawnVisiblity()
    {
        // Setzt die Alpha-Werte aller VFX-Objekte auf ihre gespeicherte Sichtbarkeit zurück
        foreach (var vfx in _spawnedObjects)
        {
            ((VfxStruct*)vfx.Value.Address)->Alpha = vfx.Value.Visibility;
        }
    }

    // Ändert die Sichtbarkeit aller VFX-Objekte auf den angegebenen Wert (0 = unsichtbar, 1 = voll sichtbar)
    private unsafe void ChangeSpawnVisibility(float visibility)
    {
        foreach (var vfx in _spawnedObjects)
        {
            ((VfxStruct*)vfx.Value.Address)->Alpha = visibility;
        }
    }

    // Merkt sich alle aktuell gespawnten VFX mit GUID, Adresse und Original-Sichtbarkeit
    private readonly Dictionary<Guid, (nint Address, float Visibility)> _spawnedObjects = [];


    // -----------------------
    // Spawning
    // -----------------------

    // Erstellt ein statisches VFX-Objekt im Spiel
    private VfxStruct* SpawnStatic(string path, Vector3 pos, Quaternion rotation, float r, float g, float b, float a, Vector3 scale)
    {
        // Erstellt ein VFX-Objekt im Spiel anhand des Pfads zur AVFX-Datei
        VfxStruct* vfx;
        fixed (byte* terminatedPath = Encoding.UTF8.GetBytes(path).NullTerminate())
        {
            // Pool-Name muss als null-terminierter String übergeben werden
            fixed (byte* pool = _pool)
            {
                // Ruft die Spiel-interne Funktion auf, um das VFX-Objekt zu erstellen
                vfx = _staticVfxCreate(terminatedPath, pool);
            }
        }

        // Prüft, ob die Erstellung erfolgreich war
        if (vfx == null)
        {
            return null;
        }

        // Initialisiert die Position, Rotation, Farbe und Skalierung des VFX-Objekts
        vfx->Position = new Vector3(pos.X, pos.Y + 1, pos.Z);
        vfx->Rotation = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);

        // Deaktiviert automatische Caster/Target-Zuweisung
        vfx->SomeFlags &= 0xF7;
        vfx->Flags |= 2;
        vfx->Red = r;
        vfx->Green = g;
        vfx->Blue = b;
        vfx->Scale = scale;

        // Setzt den Alpha-Wert (Sichtbarkeit)
        vfx->Alpha = a;

        // Startet die VFX-Ausführung (0.0f = unendlich, -1 = Endlosschleife)
        _staticVfxRun(vfx, 0.0f, -1);

        // Gibt den Pointer zum erstellten VFX-Objekt zurück
        return vfx;
    }

    // Spawn Wrapper: erzeugt ein VFX-Objekt und speichert es in der Manager-Liste
    public Guid? SpawnObject(Vector3 position, Quaternion rotation, Vector3 scale, float r = 1f, float g = 1f, float b = 1f, float a = 0.5f)
    {
        // Versucht, ein VFX-Objekt zu erstellen
        Logger.LogDebug("Trying to Spawn orb VFX at {pos}, {rot}", position, rotation);
        var vfx = SpawnStatic("bgcommon/world/common/vfx_for_event/eff/b0150_eext_y.avfx", position, rotation, r, g, b, a, scale);

        // Prüft, ob die Erstellung erfolgreich war
        if (vfx == null || (nint)vfx == nint.Zero)
        {
            Logger.LogDebug("Failed to Spawn VFX at {pos}, {rot}", position, rotation);
            return null;
        }

        // Generiert eine eindeutige GUID für das VFX-Objekt und speichert es in der Liste
        Guid guid = Guid.NewGuid();
        Logger.LogDebug("Spawned VFX at {pos}, {rot}: 0x{ptr:X}", position, rotation, (nint)vfx);

        // Speichert die Adresse und die ursprüngliche Sichtbarkeit (Alpha) des VFX-Objekts
        _spawnedObjects[guid] = ((nint)vfx, a);

        // Gibt die GUID des neu erstellten VFX-Objekts zurück
        return guid;
    }

    // -----------------------
    // Bewegung & Entfernen
    // -----------------------

    // Bewegt ein existierendes VFX-Objekt zu einer neuen Position
    public unsafe void MoveObject(Guid id, Vector3 newPosition)
    {
        // Prüft, ob die GUID in der Liste existiert
        if (_spawnedObjects.TryGetValue(id, out var vfxValue))
        {
            // Holt den Pointer zum VFX-Objekt
            if (vfxValue.Address == nint.Zero) return;
            var vfx = (VfxStruct*)vfxValue.Address;

            // Aktualisiert die Position des VFX-Objekts
            vfx->Position = newPosition with { Y = newPosition.Y + 1 };
            vfx->Flags |= 2;
        }
    }

    // Entfernt ein VFX-Objekt aus dem Spiel und der Manager-Liste
    public void DespawnObject(Guid? id)
    {
        // Prüft, ob die GUID null ist
        if (id == null) return;

        // Versucht, das VFX-Objekt anhand der GUID zu entfernen
        if (_spawnedObjects.Remove(id.Value, out var value))
        {
            Logger.LogDebug("Despawning {obj:X}", value.Address);
            _staticVfxRemove((VfxStruct*)value.Address);
        }
    }

    // Entfernt alle VFX-Objekte aus dem Spiel und der Manager-Liste
    private void RemoveAllVfx()
    {
        // Iteriert über alle gespeicherten VFX-Objekte und entfernt sie
        foreach (var obj in _spawnedObjects.Values)
        {
            Logger.LogDebug("Despawning {obj:X}", obj);
            _staticVfxRemove((VfxStruct*)obj.Address);
        }
    }

    // -----------------------
    // Cleanup
    // -----------------------

    // Dispose-Methode, die alle VFX-Objekte entfernt, wenn der Manager zerstört wird
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            RemoveAllVfx();
        }
    }

    // -----------------------
    // VfxStruct: Spiel-interne Struktur
    // -----------------------

    // Struktur, die die internen Daten eines VFX-Objekts im Spiel repräsentiert
    [StructLayout(LayoutKind.Explicit)]
    internal struct VfxStruct
    {
        [FieldOffset(0x38)] public byte Flags;              // 2 = visible, 4 = paused
        [FieldOffset(0x50)] public Vector3 Position;        // Y +1 wegen Boden (ursprünglich 0.5)
        [FieldOffset(0x60)] public Quaternion Rotation;     // W +1 wegen Boden (ursprünglich 0.5)
        [FieldOffset(0x70)] public Vector3 Scale;           // Standard 1,1,1 für 100%
        [FieldOffset(0x128)] public int ActorCaster;        // Automatischer Caster/Target (z.B. Spieler)
        [FieldOffset(0x130)] public int ActorTarget;        // Automatischer Caster/Target (z.B. Spieler)
        [FieldOffset(0x1B8)] public int StaticCaster;       // Manuelle Caster/Target (z.B. für Cutscenes)
        [FieldOffset(0x1C0)] public int StaticTarget;       // Manuelle Caster/Target (z.B. für Cutscenes)
        [FieldOffset(0x248)] public byte SomeFlags;         // 0x08 = unsichtbar, 0x10 = unsichtbar im Gpose
        [FieldOffset(0x260)] public float Red;              // Farbe
        [FieldOffset(0x264)] public float Green;            // Farbe
        [FieldOffset(0x268)] public float Blue;             // Farbe
        [FieldOffset(0x26C)] public float Alpha;            // Sichtbarkeit
    }
}
