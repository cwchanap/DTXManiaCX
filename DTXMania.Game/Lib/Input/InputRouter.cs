using System;
using System.Collections.Generic;

namespace DTXMania.Game.Lib.Input
{
    /// <summary>
    /// Routes inputs from all input sources to lane hit events
    /// Handles input state and dispatches events for lane hits
    /// </summary>
    public class InputRouter : IDisposable
    {
        private readonly List<IInputSource> _inputSources;
        private readonly KeyBindings _keyBindings;
        private bool _disposed;

        /// <summary>
        /// Raised when a lane is hit
        /// </summary>
        public event EventHandler<LaneHitEventArgs>? OnLaneHit;

        public InputRouter(KeyBindings keyBindings)
        {
            _inputSources = new List<IInputSource>();
            _keyBindings = keyBindings;
        }

        /// <summary>
        /// Adds an input source to the router
        /// </summary>
        /// <param name="source">Input source to add</param>
        public void AddInputSource(IInputSource source)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(InputRouter));
            _inputSources.Add(source);
        }

        /// <summary>
        /// Initializes all input sources
        /// </summary>
        public void Initialize()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(InputRouter));
            foreach (var source in _inputSources)
            {
                source.Initialize();
            }
        }

        /// <summary>
        /// Updates all input sources and processes input state
        /// </summary>
        public void Update()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(InputRouter));
            foreach (var source in _inputSources)
            {
                foreach (var buttonState in source.Update())
                {
                    if (buttonState.IsPressed)
                    {
                        ProcessButtonState(buttonState);
                    }
                }
            }
        }

        /// <summary>
        /// Processes a button state to determine if a lane hit event should be raised
        /// </summary>
        /// <param name="buttonState">Button state to process</param>
        private void ProcessButtonState(ButtonState buttonState)
        {
            var lane = _keyBindings.GetLane(buttonState.Id);
            
            if (lane >= 0)
            {
                OnLaneHit?.Invoke(this, new LaneHitEventArgs(lane, buttonState));
            }
        }

        /// <summary>
        /// Gets the number of input sources registered
        /// </summary>
        /// <returns>Number of input sources</returns>
        public int GetSourceCount()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(InputRouter));
            return _inputSources.Count;
        }

        /// <summary>
        /// Disposes all input sources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected implementation of Dispose pattern
        /// </summary>
        /// <param name="disposing">True if disposing managed resources</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // Dispose input sources that implement IDisposable
                foreach (var source in _inputSources)
                {
                    if (source is IDisposable disposableSource)
                    {
                        disposableSource.Dispose();
                    }
                }

                // Clear event handlers
                OnLaneHit = null;
            }

            _disposed = true;
        }
    }
}
