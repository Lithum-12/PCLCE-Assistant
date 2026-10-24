namespace PCL.Assistant;

/// <summary>
/// Fork-owned assistant page instances. There is deliberately only one chat surface;
/// history selection swaps the session data shown by that surface instead of replacing pages.
/// </summary>
internal static class AssistantPages
{
    internal static PageChatLeft? FrmChatLeft;
    internal static PageChatRight? FrmChatRight;
    internal static PageSetupAssistant? FrmSetupAssistant;
}
