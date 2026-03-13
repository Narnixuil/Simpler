using System;
using System.Threading;

namespace Simpler.Core.Context;

/// <summary>
/// Runs a delegate on a dedicated STA thread and blocks 
/// until it completes. Required for Clipboard, SendKeys, 
/// and Shell COM interop.
/// </summary>
public static class StaRunner
{
    /// <summary>
    /// Run a synchronous action on a new STA thread.
    /// Exceptions from the action are re-thrown on the caller's thread.
    /// </summary>
    public static void Run(Action action)
    {
        Exception? caught = null;

        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { caught = ex; }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (caught != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(caught).Throw();
    }

    /// <summary>
    /// Run a synchronous function on a new STA thread and 
    /// return its result.
    /// </summary>
    public static T Run<T>(Func<T> func)
    {
        T result = default!;
        Exception? caught = null;

        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { caught = ex; }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (caught != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(caught).Throw();

        return result;
    }
}
