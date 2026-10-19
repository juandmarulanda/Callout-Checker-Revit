using System;
using System.Collections.Concurrent;
using Autodesk.Revit.UI;

namespace CalloutCheck.Core
{
    /// <summary>
    /// Runs actions inside a valid Revit API context. The window is modeless, so every
    /// read/write of the model is queued here and executed through an ExternalEvent.
    /// </summary>
    internal sealed class RevitTaskQueue : IExternalEventHandler, IDisposable
    {
        private readonly ConcurrentQueue<Action<UIApplication>> _queue = new ConcurrentQueue<Action<UIApplication>>();
        private readonly ExternalEvent _event;

        public RevitTaskQueue()
        {
            _event = ExternalEvent.Create(this);
        }

        public void Run(Action<UIApplication> action)
        {
            _queue.Enqueue(action);
            _event.Raise();
        }

        public void Execute(UIApplication app)
        {
            while (_queue.TryDequeue(out Action<UIApplication> action))
            {
                try
                {
                    action(app);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    // user pressed ESC during a pick
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("CalloutCheck", ex.Message);
                }
            }
        }

        public string GetName() => "CalloutCheck task queue";

        public void Dispose() => _event?.Dispose();
    }
}
