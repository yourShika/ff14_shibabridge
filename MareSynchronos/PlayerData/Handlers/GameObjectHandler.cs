﻿using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using static FFXIVClientStructs.FFXIV.Client.Game.Character.DrawDataContainer;
using ObjectKind = MareSynchronos.API.Data.Enum.ObjectKind;

namespace MareSynchronos.PlayerData.Handlers;

public sealed class GameObjectHandler : DisposableMediatorSubscriberBase
{
    private readonly DalamudUtilService _dalamudUtil;
    private readonly Func<IntPtr> _getAddress;
    private readonly bool _isOwnedObject;
    private readonly PerformanceCollectorService _performanceCollector;
    private CancellationTokenSource? _clearCts = new();
    private Task? _delayedZoningTask;
    private bool _haltProcessing = false;
    private bool _ignoreSendAfterRedraw = false;
    private int _ptrNullCounter = 0;
    private byte _classJob = 0;
    private CancellationTokenSource _zoningCts = new();

    public GameObjectHandler(ILogger<GameObjectHandler> logger, PerformanceCollectorService performanceCollector,
        MareMediator mediator, DalamudUtilService dalamudUtil, ObjectKind objectKind, Func<IntPtr> getAddress, bool ownedObject = true) : base(logger, mediator)
    {
        _performanceCollector = performanceCollector;
        ObjectKind = objectKind;
        _dalamudUtil = dalamudUtil;
        _getAddress = () =>
        {
            _dalamudUtil.EnsureIsOnFramework();
            return getAddress.Invoke();
        };
        _isOwnedObject = ownedObject;
        Name = string.Empty;

        if (ownedObject)
        {
            Mediator.Subscribe<TransientResourceChangedMessage>(this, (msg) =>
            {
                if (_delayedZoningTask?.IsCompleted ?? true)
                {
                    if (msg.Address != Address) return;
                    Mediator.Publish(new CreateCacheForObjectMessage(this));
                }
            });
        }

        Mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => FrameworkUpdate());

        Mediator.Subscribe<ZoneSwitchEndMessage>(this, (_) => ZoneSwitchEnd());
        Mediator.Subscribe<ZoneSwitchStartMessage>(this, (_) => ZoneSwitchStart());

        Mediator.Subscribe<CutsceneStartMessage>(this, (_) =>
        {
            _haltProcessing = true;
        });
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) =>
        {
            _haltProcessing = false;
            ZoneSwitchEnd();
        });
        Mediator.Subscribe<PenumbraStartRedrawMessage>(this, (msg) =>
        {
            if (msg.Address == Address)
            {
                _haltProcessing = true;
            }
        });
        Mediator.Subscribe<PenumbraEndRedrawMessage>(this, (msg) =>
        {
            if (msg.Address == Address)
            {
                _haltProcessing = false;
                _ = Task.Run(async () =>
                {
                    _ignoreSendAfterRedraw = true;
                    await Task.Delay(500).ConfigureAwait(false);
                    _ignoreSendAfterRedraw = false;
                });
            }
        });

        Mediator.Publish(new GameObjectHandlerCreatedMessage(this, _isOwnedObject));

        _dalamudUtil.RunOnFrameworkThread(CheckAndUpdateObject).GetAwaiter().GetResult();
    }

    private enum DrawCondition
    {
        None,
        DrawObjectZero,
        RenderFlags,
        ModelInSlotLoaded,
        ModelFilesInSlotLoaded
    }

    public byte RaceId { get; private set; }
    public byte Gender { get; private set; }
    public byte TribeId { get; private set; }

    public IntPtr Address { get; private set; }
    public string Name { get; private set; }
    public ObjectKind ObjectKind { get; }
    private byte[] CustomizeData { get; set; } = new byte[26];
    private IntPtr DrawObjectAddress { get; set; }
    private byte[] EquipSlotData { get; set; } = new byte[40];
    private ushort[] MainHandData { get; set; } = new ushort[3];
    private ushort[] OffHandData { get; set; } = new ushort[3];

    public async Task ActOnFrameworkAfterEnsureNoDrawAsync(Action<Dalamud.Game.ClientState.Objects.Types.ICharacter> act, CancellationToken token)
    {
        while (await _dalamudUtil.RunOnFrameworkThread(() =>
               {
                   if (IsBeingDrawn()) return true;
                   var gameObj = _dalamudUtil.CreateGameObject(Address);
                   if (gameObj is Dalamud.Game.ClientState.Objects.Types.ICharacter chara)
                   {
                       act.Invoke(chara);
                   }
                   return false;
               }).ConfigureAwait(false))
        {
            await Task.Delay(250, token).ConfigureAwait(false);
        }
    }

    public void CompareNameAndThrow(string name)
    {
        if (!string.Equals(Name, name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Player name not equal to requested name, pointer invalid");
        }
        if (Address == IntPtr.Zero)
        {
            throw new InvalidOperationException("Player pointer is zero, pointer invalid");
        }
    }

    public IntPtr CurrentAddress()
    {
        _dalamudUtil.EnsureIsOnFramework();
        return _getAddress.Invoke();
    }

    public Dalamud.Game.ClientState.Objects.Types.IGameObject? GetGameObject()
    {
        return _dalamudUtil.CreateGameObject(Address);
    }

    public void Invalidate()
    {
        Address = IntPtr.Zero;
        DrawObjectAddress = IntPtr.Zero;
        _haltProcessing = false;
    }

    public async Task<bool> IsBeingDrawnRunOnFrameworkAsync()
    {
        return await _dalamudUtil.RunOnFrameworkThread(IsBeingDrawn).ConfigureAwait(false);
    }

    public override string ToString()
    {
        var owned = _isOwnedObject ? "Self" : "Other";
        return $"{owned}/{ObjectKind}:{Name} ({Address:X},{DrawObjectAddress:X})";
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        Mediator.Publish(new GameObjectHandlerDestroyedMessage(this, _isOwnedObject));
    }

    private unsafe void CheckAndUpdateObject()
    {
        var prevAddr = Address;
        var prevDrawObj = DrawObjectAddress;

        Address = _getAddress();
        if (Address != IntPtr.Zero)
        {
            _ptrNullCounter = 0;
            var drawObjAddr = (IntPtr)((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)Address)->DrawObject;
            DrawObjectAddress = drawObjAddr;
        }
        else
        {
            DrawObjectAddress = IntPtr.Zero;
        }

        if (_haltProcessing) return;

        bool drawObjDiff = DrawObjectAddress != prevDrawObj;
        bool addrDiff = Address != prevAddr;

        if (Address != IntPtr.Zero && DrawObjectAddress != IntPtr.Zero)
        {
            if (_clearCts != null)
            {
                Logger.LogDebug("[{this}] Cancelling Clear Task", this);
                _clearCts.CancelDispose();
                _clearCts = null;
            }
            var chara = (Character*)Address;
            var name = chara->GameObject.NameString;
            bool nameChange = !string.Equals(name, Name, StringComparison.Ordinal);
            if (nameChange)
            {
                Name = name;
            }
            bool equipDiff = false;

            if (((DrawObject*)DrawObjectAddress)->Object.GetObjectType() == ObjectType.CharacterBase
                && ((CharacterBase*)DrawObjectAddress)->GetModelType() == CharacterBase.ModelType.Human)
            {
                var classJob = chara->CharacterData.ClassJob;
                if (classJob != _classJob)
                {
                    Logger.LogTrace("[{this}] classjob changed from {old} to {new}", this, _classJob, classJob);
                    _classJob = classJob;
                    Mediator.Publish(new ClassJobChangedMessage(this));
                }

                equipDiff = CompareAndUpdateEquipByteData((byte*)&((Human*)DrawObjectAddress)->Head);

                ref var mh = ref chara->DrawData.Weapon(WeaponSlot.MainHand);
                ref var oh = ref chara->DrawData.Weapon(WeaponSlot.OffHand);
                equipDiff |= CompareAndUpdateMainHand((Weapon*)mh.DrawObject);
                equipDiff |= CompareAndUpdateOffHand((Weapon*)oh.DrawObject);

                if (equipDiff)
                    Logger.LogTrace("Checking [{this}] equip data as human from draw obj, result: {diff}", this, equipDiff);
            }
            else
            {
                equipDiff = CompareAndUpdateEquipByteData((byte*)Unsafe.AsPointer(ref chara->DrawData.EquipmentModelIds[0]));
                if (equipDiff)
                    Logger.LogTrace("Checking [{this}] equip data from game obj, result: {diff}", this, equipDiff);
            }

            if (equipDiff && !_isOwnedObject && !_ignoreSendAfterRedraw) // send the message out immediately and cancel out, no reason to continue if not self
            {
                Logger.LogTrace("[{this}] Changed", this);
                Mediator.Publish(new CharacterChangedMessage(this));
                return;
            }

            bool customizeDiff = false;

            if (((DrawObject*)DrawObjectAddress)->Object.GetObjectType() == ObjectType.CharacterBase
                && ((CharacterBase*)DrawObjectAddress)->GetModelType() == CharacterBase.ModelType.Human)
            {
                var gender = ((Human*)DrawObjectAddress)->Customize.Sex;
                var raceId = ((Human*)DrawObjectAddress)->Customize.Race;
                var tribeId = ((Human*)DrawObjectAddress)->Customize.Tribe;

                if (_isOwnedObject && ObjectKind == ObjectKind.Player
                    && (gender != Gender || raceId != RaceId || tribeId != TribeId))
                {
                    Mediator.Publish(new CensusUpdateMessage(gender, raceId, tribeId));
                    Gender = gender;
                    RaceId = raceId;
                    TribeId = tribeId;
                }

                customizeDiff = CompareAndUpdateCustomizeData(((Human*)DrawObjectAddress)->Customize.Data);
                if (customizeDiff)
                    Logger.LogTrace("Checking [{this}] customize data as human from draw obj, result: {diff}", this, customizeDiff);
            }
            else
            {
                customizeDiff = CompareAndUpdateCustomizeData(chara->DrawData.CustomizeData.Data);
                if (customizeDiff)
                    Logger.LogTrace("Checking [{this}] customize data from game obj, result: {diff}", this, equipDiff);
            }

            if ((addrDiff || drawObjDiff || equipDiff || customizeDiff || nameChange) && _isOwnedObject)
            {
                Logger.LogDebug("[{this}] Changed, Sending CreateCacheObjectMessage", this);
                Mediator.Publish(new CreateCacheForObjectMessage(this));
            }
        }
        else if (addrDiff || drawObjDiff)
        {
            Logger.LogTrace("[{this}] Changed", this);
            if (_isOwnedObject && ObjectKind != ObjectKind.Player)
            {
                _clearCts?.CancelDispose();
                _clearCts = new();
                var token = _clearCts.Token;
                _ = Task.Run(() => ClearAsync(token), token);
            }
        }
    }

    private async Task ClearAsync(CancellationToken token)
    {
        Logger.LogDebug("[{this}] Running Clear Task", this);
        await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
        Logger.LogDebug("[{this}] Sending ClearCachedForObjectMessage", this);
        Mediator.Publish(new ClearCacheForObjectMessage(this));
        _clearCts = null;
    }

    private unsafe bool CompareAndUpdateCustomizeData(Span<byte> customizeData)
    {
        bool hasChanges = false;

        for (int i = 0; i < customizeData.Length; i++)
        {
            var data = customizeData[i];
            if (CustomizeData[i] != data)
            {
                CustomizeData[i] = data;
                hasChanges = true;
            }
        }

        return hasChanges;
    }

    private unsafe bool CompareAndUpdateEquipByteData(byte* equipSlotData)
    {
        bool hasChanges = false;
        for (int i = 0; i < EquipSlotData.Length; i++)
        {
            var data = equipSlotData[i];
            if (EquipSlotData[i] != data)
            {
                EquipSlotData[i] = data;
                hasChanges = true;
            }
        }

        return hasChanges;
    }

    private unsafe bool CompareAndUpdateMainHand(Weapon* weapon)
    {
        if ((nint)weapon == nint.Zero) return false;
        bool hasChanges = false;
        hasChanges |= weapon->ModelSetId != MainHandData[0];
        MainHandData[0] = weapon->ModelSetId;
        hasChanges |= weapon->Variant != MainHandData[1];
        MainHandData[1] = weapon->Variant;
        hasChanges |= weapon->SecondaryId != MainHandData[2];
        MainHandData[2] = weapon->SecondaryId;
        return hasChanges;
    }

    private unsafe bool CompareAndUpdateOffHand(Weapon* weapon)
    {
        if ((nint)weapon == nint.Zero) return false;
        bool hasChanges = false;
        hasChanges |= weapon->ModelSetId != OffHandData[0];
        OffHandData[0] = weapon->ModelSetId;
        hasChanges |= weapon->Variant != OffHandData[1];
        OffHandData[1] = weapon->Variant;
        hasChanges |= weapon->SecondaryId != OffHandData[2];
        OffHandData[2] = weapon->SecondaryId;
        return hasChanges;
    }

    private void FrameworkUpdate()
    {
        if (!_delayedZoningTask?.IsCompleted ?? false) return;

        try
        {
            _performanceCollector.LogPerformance(this, $"CheckAndUpdateObject>{(_isOwnedObject ? "Self" : "Other")}+{ObjectKind}/{(string.IsNullOrEmpty(Name) ? "Unk" : Name)}"
                + $"+{Address.ToString("X")}", CheckAndUpdateObject);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error during FrameworkUpdate of {this}", this);
        }
    }

    private unsafe IntPtr GetDrawObjUnsafe(nint curPtr)
    {
        return (IntPtr)((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)curPtr)->DrawObject;
    }

    private bool IsBeingDrawn()
    {
        var curPtr = _getAddress();
        Logger.LogTrace("[{this}] IsBeingDrawn, CurPtr: {ptr}", this, curPtr.ToString("X"));

        if (curPtr == IntPtr.Zero && _ptrNullCounter < 2)
        {
            Logger.LogTrace("[{this}] IsBeingDrawn, CurPtr is ZERO, counter is {cnt}", this, _ptrNullCounter);
            _ptrNullCounter++;
            return true;
        }

        if (curPtr == IntPtr.Zero)
        {
            Logger.LogTrace("[{this}] IsBeingDrawn, CurPtr is ZERO, returning", this);

            Address = IntPtr.Zero;
            DrawObjectAddress = IntPtr.Zero;
            throw new ArgumentNullException($"CurPtr for {this} turned ZERO");
        }

        if (_dalamudUtil.IsAnythingDrawing)
        {
            Logger.LogTrace("[{this}] IsBeingDrawn, Global draw block", this);
            return true;
        }

        var drawObj = GetDrawObjUnsafe(curPtr);
        Logger.LogTrace("[{this}] IsBeingDrawn, DrawObjPtr: {ptr}", this, drawObj.ToString("X"));
        var isDrawn = IsBeingDrawnUnsafe(drawObj, curPtr);
        Logger.LogTrace("[{this}] IsBeingDrawn, Condition: {cond}", this, isDrawn);
        return isDrawn != DrawCondition.None;
    }

    private unsafe DrawCondition IsBeingDrawnUnsafe(IntPtr drawObj, IntPtr curPtr)
    {
        var drawObjZero = drawObj == IntPtr.Zero;
        if (drawObjZero) return DrawCondition.DrawObjectZero;
        var renderFlags = (((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)curPtr)->RenderFlags) != 0x0;
        if (renderFlags) return DrawCondition.RenderFlags;

        if (ObjectKind == ObjectKind.Player)
        {
            var modelInSlotLoaded = (((CharacterBase*)drawObj)->HasModelInSlotLoaded != 0);
            if (modelInSlotLoaded) return DrawCondition.ModelInSlotLoaded;
            var modelFilesInSlotLoaded = (((CharacterBase*)drawObj)->HasModelFilesInSlotLoaded != 0);
            if (modelFilesInSlotLoaded) return DrawCondition.ModelFilesInSlotLoaded;
            return DrawCondition.None;
        }

        return DrawCondition.None;
    }

    private void ZoneSwitchEnd()
    {
        if (!_isOwnedObject || _haltProcessing) return;

        _clearCts?.Cancel();
        _clearCts?.Dispose();
        _clearCts = null;
        try
        {
            _zoningCts?.CancelAfter(2500);
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Zoning CTS cancel issue");
        }
    }

    private void ZoneSwitchStart()
    {
        if (!_isOwnedObject || _haltProcessing) return;

        _zoningCts = new();
        Logger.LogDebug("[{obj}] Starting Delay After Zoning", this);
        _delayedZoningTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(120), _zoningCts.Token).ConfigureAwait(false);
            }
            catch
            {
                // ignore cancelled
            }
            finally
            {
                Logger.LogDebug("[{this}] Delay after zoning complete", this);
                _zoningCts.Dispose();
            }
        });
    }
}