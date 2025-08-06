using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DTXMania.Game.Lib.Input
{
    /// <summary>
    /// Routes inputs from all input sources to lane hit events
    /// Handles input state and dispatches events for lane hits
    /// </summary>
    public class InputRouter
    {
        private readonly List<IInputSource> _inputSources;
        private readonly KeyBindings _keyBindings;

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
            _inputSources.Add(source);
        }

        /// <summary>
        /// Initializes all input sources
        /// </summary>
        public void Initialize()
        {
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
            return _inputSources.Count;
        }

        /// <summary>
        /// Disposes all input sources
        /// </summary>
        public void Dispose()
        {
            foreach (var source in _inputSources)
            {
                source.Dispose();
            }
        }
    }
}
