// GlobalSuppressions - part of ShibaBridge project.
// Dieses File dient dazu, Code-Analyse-Warnungen (Roslyn Analyzers) projektweit zu unterdrücken.
// So bleibt der Quellcode selbst sauber, ohne dass überall SuppressMessage-Attribute eingefügt werden müssen.

using System.Diagnostics.CodeAnalysis; // Namespace für Code-Analyse-Attribute

// Assembly-Attribut: SuppressMessage
// Dieses Attribut wird auf Assembly-Ebene angewendet, um eine bestimmte Warnung (CA1416) global zu unterdrücken.
[assembly: SuppressMessage(
    "Interoperability", // Kategorie der Regel (hier: Interoperabilität)
    "CA1416:Validate platform compatibility", // Regel-ID + Titel
    Justification = "<Pending>", // Begründung, warum die Unterdrückung erlaubt ist (hier noch Platzhalter "<Pending>")
    Scope = "member", // Gültigkeitsbereich: hier nur für ein bestimmtes Member (Methode)
    Target = "~M:ShibaBridge.Services.CharaDataManager.AttachPoseData(ShibaBridge.API.Dto.CharaData.PoseEntry,ShibaBridge.Services.CharaData.Models.CharaDataExtendedUpdateDto)"
// Ziel: die Methode `AttachPoseData` in der Klasse `CharaDataManager` mit den angegebenen Parametern
)]