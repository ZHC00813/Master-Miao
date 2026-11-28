namespace Blankedge.SWHoleTools.Model
{
    public enum HoleSemanticType
    {
        Normal,
        Threaded,
        Clearance,
        Dowel,
        Counterbore,
        Countersink,
        Custom, ExternalThread
    }

    public enum TerminationType
    {
        Through,
        Blind,
        InterBody,
        PartialThrough,
        Ambiguous
    }

    public enum HoleBottomType
    {
        None,
        Flat,
        Conical,
        Other,
        Unknown
    }

    public enum HoleColorCode
    {
        M3,
        M4,
        M5,
        M6,
        ClearanceHole,
        Counterbore,
        Countersink,
        DowelHole,
        Custom
    }
}

