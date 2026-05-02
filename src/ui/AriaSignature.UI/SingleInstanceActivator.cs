namespace AriaSignature.UI;

internal static class SingleInstanceActivator
{
    public static void SignalExistingInstance(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return;
        }

        try
        {
            using var eventHandle = EventWaitHandle.OpenExisting(eventName);
            eventHandle.Set();
        }
        catch
        {
            // best-effort: if signaling fails, the new instance exits silently
        }
    }
}
