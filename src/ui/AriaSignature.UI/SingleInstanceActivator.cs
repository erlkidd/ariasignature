namespace AriaSignature.UI;

internal static class SingleInstanceActivator
{
    public static bool SignalExistingInstance(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return false;
        }

        try
        {
            using var eventHandle = EventWaitHandle.OpenExisting(eventName);
            eventHandle.Set();
            return true;
        }
        catch
        {
            // best-effort: if signaling fails, caller can decide fallback behavior
            return false;
        }
    }
}
