namespace MpFoundation;

public static class ScenePaths
{
    public const string Splash = "res://scenes/ui/Splash.tscn";

    // The menu scene plays BOTH former screens: its title tier is what TitleScreen was,
    // its options tier is what MainMenu was. Both constants point at it so every existing
    // caller — Splash's hand-off, and the Back button on Host / Join / Settings — keeps
    // working untouched. The scene itself remembers whether the player is past the title, so
    // a Back press returns to the options rather than to "press start".
    public const string Title = "res://scenes/ui/MainMenu.tscn";
    public const string MainMenu = "res://scenes/ui/MainMenu.tscn";
    public const string HostMenu = "res://scenes/ui/HostMenu.tscn";
    public const string JoinMenu = "res://scenes/ui/JoinMenu.tscn";
    public const string SettingsMenu = "res://scenes/ui/SettingsMenu.tscn";
    public const string HowToPlayPanel = "res://scenes/ui/HowToPlayPanel.tscn";
    public const string PlaytestForewordPanel = "res://scenes/ui/PlaytestForewordPanel.tscn";
    public const string UsageNoticePanel = "res://scenes/ui/UsageNoticePanel.tscn";
    public const string Gameplay = "res://scenes/game/Gameplay.tscn";
    public const string NetworkedAvatar = "res://scenes/game/NetworkedAvatar.tscn";

    // BT-0 (2026-08-27): the Bubble Test playtest level — the only world the MVP extraction kept. A seam file — it instances seven section
    // scenes and authors no geometry of its own (program D3); BT-1..5 and BT-10 each replace one
    // section's contents, keeping the file name and root type. TANGLE-1 (2026-08-29) added the
    // seventh, Tangle.tscn, as a new file rather than by replacing one — the count moved with it.
    public const string BubbleTest = "res://scenes/game/world/bubbletest/BubbleTest.tscn";
}
