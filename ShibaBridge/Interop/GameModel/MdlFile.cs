// MdlFile - Teil des ShibaBridge Projekts
// Zweck:
//   - Lädt und verarbeitet Final Fantasy XIV Model-Dateien (MDL).
//   - Unterstützt Versionen V5 und V6.
//   - Stellt Strukturen und Flags bereit, um Geometrie, Meshes, LODs und weitere
//     Metadaten auszulesen.
//   - Code wurde von Penumbra übernommen, um die gleiche Kompatibilität sicherzustellen.

using Lumina.Data;
using Lumina.Extensions;
using System.Runtime.InteropServices;
using System.Text;
using static Lumina.Data.Parsing.MdlStructs;

namespace ShibaBridge.Interop.GameModel;

#pragma warning disable S1104 // Öffentliche Felder (bewusst zur direkten Nutzung)

// Repräsentiert eine MDL-Datei und bietet Parsing-Logik
public class MdlFile
{
    // Supported versions
    public const int V5 = 0x01000005;
    public const int V6 = 0x01000006;

    // Konstanten für die Struktur
    public const uint NumVertices = 17;
    public const uint FileHeaderSize = 0x44;

    // Felder der Klasse
    public uint Version = 0x01000005;
    public float Radius;
    public float ModelClipOutDistance;
    public float ShadowClipOutDistance;
    public byte BgChangeMaterialIndex;
    public byte BgCrestChangeMaterialIndex;
    public ushort CullingGridCount;
    public byte Flags3;
    public byte Unknown6;
    public ushort Unknown8;
    public ushort Unknown9;

    // Offsets und Größen für Vertex- und Index-Daten
    public uint[] VertexOffset = [0, 0, 0];
    public uint[] IndexOffset = [0, 0, 0];
    public uint[] VertexBufferSize = [0, 0, 0];
    public uint[] IndexBufferSize = [0, 0, 0];

    // Anzahl der LOD-Stufen
    public byte LodCount;
    public bool EnableIndexBufferStreaming;
    public bool EnableEdgeGeometry;

    // Flags zur Steuerung von Modellverhalten
    public ModelFlags1 Flags1;
    public ModelFlags2 Flags2;

    // Strukturen zur Beschreibung von Vertex-Deklarationen, Element-IDs, Meshes und LODs
    public VertexDeclarationStruct[] VertexDeclarations = [];
    public ElementIdStruct[] ElementIds = [];
    public MeshStruct[] Meshes = [];
    public BoundingBoxStruct[] BoneBoundingBoxes = [];
    public LodStruct[] Lods = [];
    public ExtraLodStruct[] ExtraLods = [];

    /// <summary>
    /// Lädt und parst die MDL-Datei ab Pfad.
    /// </summary>
    public MdlFile(string filePath)
    {
        // Datei öffnen und einen Binärleser erstellen
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var r = new LuminaBinaryReader(stream);

        // Header der Datei laden und Version prüfen
        var header = LoadModelFileHeader(r);
        LodCount = header.LodCount;
        VertexBufferSize = header.VertexBufferSize;
        IndexBufferSize = header.IndexBufferSize;
        VertexOffset = header.VertexOffset;
        IndexOffset = header.IndexOffset;

        // Offsets anpassen, um den Datenbereich zu berücksichtigen
        var dataOffset = FileHeaderSize + header.RuntimeSize + header.StackSize;
        for (var i = 0; i < LodCount; ++i)
        {
            VertexOffset[i] -= dataOffset;
            IndexOffset[i] -= dataOffset;
        }

        // Vertex-Deklarationen laden
        VertexDeclarations = new VertexDeclarationStruct[header.VertexDeclarationCount];
        for (var i = 0; i < header.VertexDeclarationCount; ++i)
            VertexDeclarations[i] = VertexDeclarationStruct.Read(r);

        // Strings laden (werden aktuell nicht verwendet)
        _ = LoadStrings(r);

        //ModelHeader einlesen (MeshCount, Flags, etc.)
        var modelHeader = LoadModelHeader(r);
        ElementIds = new ElementIdStruct[modelHeader.ElementIdCount];
        for (var i = 0; i < modelHeader.ElementIdCount; i++)
            ElementIds[i] = ElementIdStruct.Read(r);

        // LODs einlesen (bis zu 3 Stück)
        Lods = new LodStruct[3];
        for (var i = 0; i < 3; i++)
        {
            // LOD-Struktur einlesen
            var lod = r.ReadStructure<LodStruct>();
            if (i < LodCount)
            {
                lod.VertexDataOffset -= dataOffset;
                lod.IndexDataOffset -= dataOffset;
            }

            // Bounding-Boxen der Knochen einlesen
            Lods[i] = lod;
        }

        // Extra LODs einlesen, falls aktiviert
        ExtraLods = modelHeader.Flags2.HasFlag(ModelFlags2.ExtraLodEnabled)
            ? r.ReadStructuresAsArray<ExtraLodStruct>(3)
            : [];

        // Mesh-Strukturen einlesen
        Meshes = new MeshStruct[modelHeader.MeshCount];
        for (var i = 0; i < modelHeader.MeshCount; i++)
            Meshes[i] = MeshStruct.Read(r);
    }

    //---------------------------------------------------------------
    // Private Hilfsmethoden zum Laden von Strukturen
    //---------------------------------------------------------------

    // Lädt den ModelFileHeader und setzt relevante Felder
    private ModelFileHeader LoadModelFileHeader(LuminaBinaryReader r)
    {
        // Datei-Header einlesen
        var header = ModelFileHeader.Read(r);

        // Versionsprüfung
        Version = header.Version;
        EnableIndexBufferStreaming = header.EnableIndexBufferStreaming;
        EnableEdgeGeometry = header.EnableEdgeGeometry;

        return header;
    }

    // Lädt den ModelHeader und setzt relevante Felder
    private ModelHeader LoadModelHeader(BinaryReader r)
    {
        // ModelHeader einlesen
        var modelHeader = r.ReadStructure<ModelHeader>();

        // Relevante Felder setzen
        Radius = modelHeader.Radius;
        
        Flags1 = modelHeader.Flags1;
        Flags2 = modelHeader.Flags2;
        Flags3 = modelHeader.Flags3;
        
        ModelClipOutDistance = modelHeader.ModelClipOutDistance;
        ShadowClipOutDistance = modelHeader.ShadowClipOutDistance;
        CullingGridCount = modelHeader.CullingGridCount;

        Unknown6 = modelHeader.Unknown6;
        Unknown8 = modelHeader.Unknown8;
        Unknown9 = modelHeader.Unknown9;
        
        BgChangeMaterialIndex = modelHeader.BGChangeMaterialIndex;
        BgCrestChangeMaterialIndex = modelHeader.BGCrestChangeMaterialIndex;

        return modelHeader;
    }

    // Lädt Strings aus dem Binärleser und gibt Offsets und Strings zurück
    private static (uint[], string[]) LoadStrings(BinaryReader r)
    {
        // String-Abschnitt einlesen
        var stringCount = r.ReadUInt16();
        r.ReadUInt16();

        // Größe und Daten der Strings einlesen
        var stringSize = (int)r.ReadUInt32();
        var stringData = r.ReadBytes(stringSize);

        // Strings und Offsets extrahieren
        var start = 0;
        var strings = new string[stringCount];
        var offsets = new uint[stringCount];

        // Strings aus dem Byte-Array extrahieren
        for (var i = 0; i < stringCount; ++i)
        {
            // Finde das Ende des Strings (Null-Terminierung)
            var span = stringData.AsSpan(start);
            var idx = span.IndexOf((byte)'\0');

            // String extrahieren und Offset speichern
            strings[i] = Encoding.UTF8.GetString(span[..idx]);
            offsets[i] = (uint)start;

            // Nächsten Startpunkt setzen
            start = start + idx + 1;
        }

        // Rückgabe der Offsets und Strings
        return (offsets, strings);
    }

    // Struktur für den ModelHeader (enthält viele Basiswerte)
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ModelHeader
    {
        // MeshHeader
        public float Radius;
        public ushort MeshCount;
        public ushort AttributeCount;
        public ushort SubmeshCount;
        public ushort MaterialCount;
        public ushort BoneCount;
        public ushort BoneTableCount;
        public ushort ShapeCount;
        public ushort ShapeMeshCount;
        public ushort ShapeValueCount;
        public byte LodCount;
        public ModelFlags1 Flags1;
        public ushort ElementIdCount;
        public byte TerrainShadowMeshCount;
        public ModelFlags2 Flags2;
        public float ModelClipOutDistance;
        public float ShadowClipOutDistance;
        public ushort CullingGridCount;
        public ushort TerrainShadowSubmeshCount;
        public byte Flags3;
        public byte BGChangeMaterialIndex;
        public byte BGCrestChangeMaterialIndex;
        public byte Unknown6;
        public ushort BoneTableArrayCountTotal;
        public ushort Unknown8;
        public ushort Unknown9;
        private fixed byte _padding[6];
    }

    // Repräsentiert Shape-Daten eines Modells (Morph Targets)
    public struct ShapeStruct
    {
        // Offset zum Namen des Shapes
        public uint StringOffset;
        public ushort[] ShapeMeshStartIndex;
        public ushort[] ShapeMeshCount;

        // Liest die ShapeStruct aus dem Binärleser
        public static ShapeStruct Read(LuminaBinaryReader br)
        {
            // ShapeStruct einlesen
            ShapeStruct ret = new ShapeStruct();

            // Daten einlesen
            ret.StringOffset = br.ReadUInt32();
            ret.ShapeMeshStartIndex = br.ReadUInt16Array(3);
            ret.ShapeMeshCount = br.ReadUInt16Array(3);

            // Rückgabe der Struktur
            return ret;
        }
    }

    // Flags zur Steuerung bestimmter Rendering-Features
    [Flags]
    public enum ModelFlags1 : byte
    {
        DustOcclusionEnabled = 0x80,
        SnowOcclusionEnabled = 0x40,
        RainOcclusionEnabled = 0x20,
        Unknown1 = 0x10,
        LightingReflectionEnabled = 0x08,
        WavingAnimationDisabled = 0x04,
        LightShadowDisabled = 0x02,
        ShadowDisabled = 0x01,
    }

    [Flags]
    public enum ModelFlags2 : byte
    {
        Unknown2 = 0x80,
        BgUvScrollEnabled = 0x40,
        EnableForceNonResident = 0x20,
        ExtraLodEnabled = 0x10,
        ShadowMaskEnabled = 0x08,
        ForceLodRangeEnabled = 0x04,
        EdgeGeometryEnabled = 0x02,
        Unknown3 = 0x01
    }

    // Struktur zur Beschreibung eines Vertex-Elements
    public struct VertexDeclarationStruct
    {
        // Da sind immer 17 Elemente, aber wir brauchen nur die bis zum Ende
        public VertexElement[] VertexElements;

        // Liest die VertexDeclarationStruct aus dem Binärleser
        public static VertexDeclarationStruct Read(LuminaBinaryReader br)
        {
            VertexDeclarationStruct ret = new VertexDeclarationStruct();

            // Liste für die Vertex-Elemente
            var elems = new List<VertexElement>();

            // Lese die Vertex-Elemente, bis wir das Ende erreichen (Stream == 255)
            var thisElem = br.ReadStructure<VertexElement>();
            do
            {
                elems.Add(thisElem);
                thisElem = br.ReadStructure<VertexElement>();
            } 
            while 
            (
            thisElem.Stream != 255
            );

            // Überspringe die restlichen Elemente, um auf 17 zu kommen
            // Jedes Element ist 8 Bytes groß (4 + 2 + 1 + 1)
            int toSeek = 17 * 8 - (elems.Count + 1) * 8;
            br.Seek(br.BaseStream.Position + toSeek);

            // Setze die Elemente und gib die Struktur zurück
            ret.VertexElements = elems.ToArray();

            return ret;
        }
    }
}
#pragma warning restore S1104 // Öffentliche Felder