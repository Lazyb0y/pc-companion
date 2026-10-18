using System;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.System.Power;
using Windows.Win32.System.Threading;

namespace PCCompanion.Platform.Windows;

/// <summary>
/// Keeps the displays on and the PC from going to sleep on its own until disposed, with a power
/// request that <c>powercfg /requests</c> lists with its reason. It changes no setting: Windows
/// ends the request when the process exits, and when the user puts the PC to sleep.
/// </summary>
public sealed class DisplayRequest : IDisposable
{
    private readonly SafeFileHandle _request;

    private DisplayRequest(SafeFileHandle request)
    {
        _request = request;
    }

    /// <summary>
    /// Starts keeping the displays on. Returns <see langword="null"/> when Windows refuses.
    /// </summary>
    /// <param name="reason">Why, in the user's language, for <c>powercfg /requests</c>.</param>
    public static unsafe DisplayRequest? Start(string reason)
    {
        ArgumentException.ThrowIfNullOrEmpty(reason);

        SafeFileHandle request;
        fixed (char* text = reason)
        {
            var context = new REASON_CONTEXT
            {
                Version = PInvoke.POWER_REQUEST_CONTEXT_VERSION,
                Flags = POWER_REQUEST_CONTEXT_FLAGS.POWER_REQUEST_CONTEXT_SIMPLE_STRING,
            };
            context.Reason.SimpleReasonString = text;
            request = PInvoke.PowerCreateRequest(context);
        }

        if (request.IsInvalid)
        {
            request.Dispose();
            return null;
        }

        // A display request alone doesn't keep the PC from going to sleep, so it needs a system
        // request too.
        if (!PInvoke.PowerSetRequest(request, POWER_REQUEST_TYPE.PowerRequestDisplayRequired))
        {
            request.Dispose();
            return null;
        }

        if (!PInvoke.PowerSetRequest(request, POWER_REQUEST_TYPE.PowerRequestSystemRequired))
        {
            PInvoke.PowerClearRequest(request, POWER_REQUEST_TYPE.PowerRequestDisplayRequired);
            request.Dispose();
            return null;
        }

        return new DisplayRequest(request);
    }

    public void Dispose()
    {
        if (_request.IsClosed)
        {
            return;
        }

        PInvoke.PowerClearRequest(_request, POWER_REQUEST_TYPE.PowerRequestSystemRequired);
        PInvoke.PowerClearRequest(_request, POWER_REQUEST_TYPE.PowerRequestDisplayRequired);
        _request.Dispose();
    }
}
