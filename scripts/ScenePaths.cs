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

    // BASE-1 (2026-09-19): the three rooms the game is played in, and the only world that is not
    // CI scaffolding. A seam file — it instances the three room scenes and authors no geometry of
    // its own, so each room has one owner and one file and a later lane changes a room by opening
    // that room.
    public const string Supermarket = "res://scenes/game/world/supermarket/Supermarket.tscn";
}
