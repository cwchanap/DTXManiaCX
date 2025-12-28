using System;

namespace DTXMania.Game.Lib.Song.Entities
{
    /// <summary>
    /// Enumeration that represents the judgement types in DTXMania gameplay.
    /// Based on DTXManiaNX judgement system patterns.
    /// </summary>
    public enum JudgementType
    {
        /// <summary>
        /// Perfect timing hit (highest accuracy)
        /// </summary>
        Just,
        
        /// <summary>
        /// Excellent timing hit (very good accuracy)
        /// </summary>
        Great,
        
        /// <summary>
        /// Good timing hit (acceptable accuracy)
        /// </summary>
        Good,
        
        /// <summary>
        /// Poor timing hit (low accuracy, still counts as hit)
        /// </summary>
        Poor,
        
        /// <summary>
        /// Missed note (no hit registered)
        /// </summary>
        Miss
    }

    /// <summary>
    /// Event payload for a judgement made during gameplay.
    /// Contains all necessary information for scoring and feedback systems.
    /// </summary>
    public class JudgementEvent
    {
        #region Properties
        
        /// <summary>
        /// Reference to the note that was judged (for tracking purposes)
        /// </summary>
        public int NoteRef { get; set; }
        
        /// <summary>
        /// The lane where the judgement occurred (0-9 for the 10 lanes)
        /// </summary>
        public int Lane { get; set; }
        
        /// <summary>
        /// Timing delta in milliseconds (positive = late, negative = early)
        /// </summary>
        public double DeltaMs { get; set; }
        
        /// <summary>
        /// Type of the judgement (Just/Great/Good/Poor/Miss)
        /// </summary>
        public JudgementType Type { get; set; }
        
        /// <summary>
        /// Event timestamp when the judgement occurred
        /// </summary>
        public DateTime Timestamp { get; set; }
        
        #endregion
        
        #region Constructors
        
        /// <summary>
        /// Default constructor for JudgementEvent
        /// </summary>
        public JudgementEvent()
        {
            Type = JudgementType.Miss;
            Timestamp = DateTime.UtcNow;
        }
        
        /// <summary>
        /// Creates a new JudgementEvent with specified parameters
        /// </summary>
        /// <param name="noteRef">Reference to the note</param>
        /// <param name="lane">Lane where judgement occurred</param>
        /// <param name="deltaMs">Timing delta in milliseconds</param>
        /// <param name="type">Type of judgement</param>
        public JudgementEvent(int noteRef, int lane, double deltaMs, JudgementType type)
        {
            NoteRef = noteRef;
            Lane = lane;
            DeltaMs = deltaMs;
            Type = type;
            Timestamp = DateTime.UtcNow;
        }
        
        /// <summary>
        /// Creates a new JudgementEvent with automatic judgement type calculation
        /// </summary>
        /// <param name="noteRef">Reference to the note</param>
        /// <param name="lane">Lane where judgement occurred</param>
        /// <param name="deltaMs">Timing delta in milliseconds</param>
        public JudgementEvent(int noteRef, int lane, double deltaMs) 
            : this(noteRef, lane, deltaMs, TimingConstants.GetJudgementType(deltaMs))
        {
        }
        
        #endregion
        
        #region Methods
        
        /// <summary>
        /// Gets the score value for this judgement event
        /// </summary>
        /// <returns>Score points for this judgement</returns>
        public int GetScoreValue()
        {
            return TimingConstants.GetScoreValue(Type);
        }
        
        /// <summary>
        /// Determines if this judgement represents a hit (not a miss)
        /// </summary>
        /// <returns>True if the note was hit, false if missed</returns>
        public bool IsHit()
        {
            return Type != JudgementType.Miss;
        }
        
        /// <summary>
        /// Determines if this judgement was early (negative delta)
        /// </summary>
        /// <returns>True if the hit was early</returns>
        public bool IsEarly()
        {
            return DeltaMs < 0;
        }
        
        /// <summary>
        /// Determines if this judgement was late (positive delta)
        /// </summary>
        /// <returns>True if the hit was late</returns>
        public bool IsLate()
        {
            return DeltaMs > 0;
        }
        
        /// <summary>
        /// Gets the absolute timing deviation
        /// </summary>
        /// <returns>Absolute value of timing delta</returns>
        public double GetAbsoluteDelta()
        {
            return Math.Abs(DeltaMs);
        }
        
        /// <summary>
        /// Returns a string representation of this judgement event
        /// </summary>
        /// <returns>Formatted string with event details</returns>
        public override string ToString()
        {
            var inputLane = InputLaneExtensions.FromLaneIndex(Lane);
            var laneDisplay = inputLane?.GetDisplayName() ?? $"Lane {Lane}";
            var timingText = IsEarly() ? "early" : (IsLate() ? "late" : "perfect");
            
            return $"JudgementEvent[{Type}] {laneDisplay} ({DeltaMs:+0.0;-0.0;0.0}ms {timingText}) - {GetScoreValue()} pts";
        }
        
        #endregion
    }

    /// <summary>
    /// Static class containing timing constants for judgement windows.
    /// Based on DTXManiaNX timing specifications for accurate rhythm game scoring.
    /// All values are in milliseconds and represent the acceptable timing deviation from perfect hit timing.
    /// </summary>
    public static class TimingConstants
    {
        #region Judgement Windows (in milliseconds)
        
        /// <summary>
        /// Perfect timing window for "Just" judgement (±25ms)
        /// </summary>
        public const double JustWindowMs = 25.0;
        
        /// <summary>
        /// Great timing window for "Great" judgement (±50ms)
        /// </summary>
        public const double GreatWindowMs = 50.0;
        
        /// <summary>
        /// Good timing window for "Good" judgement (±100ms)
        /// </summary>
        public const double GoodWindowMs = 100.0;
        
        /// <summary>
        /// Poor timing window for "Poor" judgement (±150ms)
        /// </summary>
        public const double PoorWindowMs = 150.0;
        
        /// <summary>
        /// Miss threshold - notes beyond this timing are considered missed (±200ms)
        /// </summary>
        public const double MissThresholdMs = 200.0;
        
        #endregion
        
        #region Score Values
        
        /// <summary>
        /// Score points awarded for a "Just" hit
        /// </summary>
        public const int JustScore = 1000;
        
        /// <summary>
        /// Score points awarded for a "Great" hit
        /// </summary>
        public const int GreatScore = 700;
        
        /// <summary>
        /// Score points awarded for a "Good" hit
        /// </summary>
        public const int GoodScore = 400;
        
        /// <summary>
        /// Score points awarded for a "Poor" hit
        /// </summary>
        public const int PoorScore = 100;
        
        /// <summary>
        /// Score points awarded for a "Miss" (typically 0)
        /// </summary>
        public const int MissScore = 0;
        
        #endregion
        
        #region Helper Methods
        
        /// <summary>
        /// Determines the judgement type based on timing delta
        /// </summary>
        /// <param name="deltaMs">Timing delta in milliseconds (absolute value)</param>
        /// <returns>The appropriate judgement type</returns>
        public static JudgementType GetJudgementType(double deltaMs)
        {
            double absDelta = Math.Abs(deltaMs);
            
            if (absDelta <= JustWindowMs) return JudgementType.Just;
            if (absDelta <= GreatWindowMs) return JudgementType.Great;
            if (absDelta <= GoodWindowMs) return JudgementType.Good;
            if (absDelta <= PoorWindowMs) return JudgementType.Poor;
            
            return JudgementType.Miss;
        }
        
        /// <summary>
        /// Gets the score value for a specific judgement type
        /// </summary>
        /// <param name="judgementType">The judgement type</param>
        /// <returns>Score value for the judgement</returns>
        public static int GetScoreValue(JudgementType judgementType)
        {
            return judgementType switch
            {
                JudgementType.Just => JustScore,
                JudgementType.Great => GreatScore,
                JudgementType.Good => GoodScore,
                JudgementType.Poor => PoorScore,
                JudgementType.Miss => MissScore,
                _ => 0
            };
        }
        
        #endregion
    }

    /// <summary>
    /// Enum for input lanes, mapping to specific DTX channels.
    /// Based on DTXMania 10-lane layout (channels 11-1C) for drum gameplay.
    /// Updated to match DTXChartParser channel-to-lane mapping.
    /// </summary>
    public enum InputLane
    {
        /// <summary>
        /// Splash/Crash (DTX Channel 1A, Lane Index 0)
        /// </summary>
        Splash = 0x1A,
        
        /// <summary>
        /// Floor Tom & Left Cymbal (DTX Channels 18&11, Lane Index 1)
        /// </summary>
        FloorTomLeftCymbal = 0x18, // Use Floor Tom as primary for enum value
        
        /// <summary>
        /// Hi-Hat Foot & Left Crash (DTX Channels 1B&1C, Lane Index 2)
        /// </summary>
        HiHatFootLeftCrash = 0x1B, // Use Hi-Hat Foot as primary for enum value
        
        /// <summary>
        /// Left Pedal (DTX Channel 12, Lane Index 3)
        /// </summary>
        LeftPedal = 0x12,
        
        /// <summary>
        /// Snare Drum (DTX Channel 14, Lane Index 4)
        /// </summary>
        SnareDrum = 0x14,
        
        /// <summary>
        /// Hi-Hat (DTX Channel 13, Lane Index 5)
        /// </summary>
        HiHat = 0x13,
        
        /// <summary>
        /// Bass Drum (DTX Channel 15, Lane Index 6)
        /// </summary>
        BassDrum = 0x15,
        
        /// <summary>
        /// High Tom (DTX Channel 16, Lane Index 7)
        /// </summary>
        HighTom = 0x16,
        
        /// <summary>
        /// Low Tom & Right Cymbal (DTX Channels 17&19, Lane Index 8)
        /// </summary>
        LowTomRightCymbal = 0x17 // Use Low Tom as primary for enum value
    }
    
    /// <summary>
    /// Utility methods for working with InputLane enum
    /// </summary>
    public static class InputLaneExtensions
    {
        /// <summary>
        /// Converts DTX channel number to InputLane enum
        /// </summary>
        /// <param name="channel">DTX channel number</param>
        /// <returns>Corresponding InputLane, or null if invalid</returns>
        public static InputLane? FromChannel(int channel)
        {
            return channel switch
            {
                0x1A => InputLane.Splash,
                0x18 => InputLane.FloorTomLeftCymbal,
                0x11 => InputLane.FloorTomLeftCymbal, // Same lane as Floor Tom
                0x1B => InputLane.HiHatFootLeftCrash,
                0x1C => InputLane.HiHatFootLeftCrash, // Same lane as Hi-Hat Foot
                0x12 => InputLane.LeftPedal,
                0x14 => InputLane.SnareDrum,
                0x13 => InputLane.HiHat,
                0x15 => InputLane.BassDrum,
                0x16 => InputLane.HighTom,
                0x17 => InputLane.LowTomRightCymbal,
                0x19 => InputLane.LowTomRightCymbal, // Same lane as Low Tom
                _ => null
            };
        }
        
        /// <summary>
        /// Converts InputLane to lane index (0-9) using DTXChartParser mapping
        /// </summary>
        /// <param name="lane">InputLane enum value</param>
        /// <returns>Lane index (0-9)</returns>
        public static int ToLaneIndex(this InputLane lane)
        {
            return lane switch
            {
                InputLane.Splash => 0,                  // 1A → Lane 0
                InputLane.FloorTomLeftCymbal => 1,       // 18&11 → Lane 1
                InputLane.HiHatFootLeftCrash => 2,       // 1B&1C → Lane 2
                InputLane.LeftPedal => 3,                // 12 → Lane 3
                InputLane.SnareDrum => 4,                // 14 → Lane 4
                InputLane.HiHat => 5,                    // 13 → Lane 5
                InputLane.BassDrum => 6,                 // 15 → Lane 6
                InputLane.HighTom => 7,                  // 16 → Lane 7
                InputLane.LowTomRightCymbal => 8,        // 17&19 → Lane 8
                _ => -1
            };
        }
        
        /// <summary>
        /// Converts lane index (0-9) to InputLane using DTXChartParser mapping
        /// </summary>
        /// <param name="laneIndex">Lane index (0-9)</param>
        /// <returns>Corresponding InputLane, or null if invalid</returns>
        public static InputLane? FromLaneIndex(int laneIndex)
        {
            return laneIndex switch
            {
                0 => InputLane.Splash,                  // Lane 0 → 1A
                1 => InputLane.FloorTomLeftCymbal,       // Lane 1 → 18&11
                2 => InputLane.HiHatFootLeftCrash,       // Lane 2 → 1B&1C
                3 => InputLane.LeftPedal,                // Lane 3 → 12
                4 => InputLane.SnareDrum,                // Lane 4 → 14
                5 => InputLane.HiHat,                    // Lane 5 → 13
                6 => InputLane.BassDrum,                 // Lane 6 → 15
                7 => InputLane.HighTom,                  // Lane 7 → 16
                8 => InputLane.LowTomRightCymbal,        // Lane 8 → 17&19
                _ => null
            };
        }
        
        /// <summary>
        /// Gets the display name for an InputLane
        /// </summary>
        /// <param name="lane">InputLane enum value</param>
        /// <returns>Human-readable name</returns>
        public static string GetDisplayName(this InputLane lane)
        {
            return lane switch
            {
                InputLane.Splash => "Splash/Crash",
                InputLane.FloorTomLeftCymbal => "Floor Tom/Left Cymbal",
                InputLane.HiHatFootLeftCrash => "Hi-Hat Foot/Left Crash",
                InputLane.LeftPedal => "Left Pedal",
                InputLane.SnareDrum => "Snare Drum",
                InputLane.HiHat => "Hi-Hat",
                InputLane.BassDrum => "Bass Drum",
                InputLane.HighTom => "High Tom",
                InputLane.LowTomRightCymbal => "Low Tom/Right Cymbal",
                _ => "Unknown"
            };
        }
    }
}
