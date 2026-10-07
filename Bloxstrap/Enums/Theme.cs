namespace Bloxstrap.Enums
{
    public enum Theme
    {
        [EnumName(FromTranslation = "Common.SystemDefault")]
        Default,
        Dark,
        Light,
        [EnumName(StaticName = "Crimson Contract")]
        CrimsonContract,
        Purple,
        Blue,
        Green,
        Orange,
        Pink,
        [EnumName(FromTranslation = "Common.Custom")]
        Custom
    }
}
