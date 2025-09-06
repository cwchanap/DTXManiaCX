using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Drawing;

namespace DTXManiaCX.MCP.Server.Services;

/// <summary>
/// Service for interacting with .NET game applications (like MonoGame)
/// Provides tools for clicking, input simulation, and game state inspection
/// </summary>
public class GameInteractionService
{
    private readonly ILogger<GameInteractionService> _logger;
    private readonly GameStateManager _gameStateManager;
    private readonly bool _isWindows;
    
    // Windows API imports for mouse/keyboard simulation
    [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);
    
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
    
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);
    
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rectangle lpRect);
    
    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);
    
    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref Point lpPoint);
    
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    
    // Mouse event flags
    private const uint MOUSEEVENTF_LEFTDOWN = 0x02;
    private const uint MOUSEEVENTF_LEFTUP = 0x04;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x08;
    private const uint MOUSEEVENTF_RIGHTUP = 0x10;
    private const uint MOUSEEVENTF_MOVE = 0x01;
    
    public GameInteractionService(ILogger<GameInteractionService> logger, GameStateManager gameStateManager)
    {
        _logger = logger;
        _gameStateManager = gameStateManager;
        _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        
        if (!_isWindows)
        {
            _logger.LogWarning("Game interaction service running on non-Windows platform. Some features may be limited.");
        }
    }
    
    /// <summary>
    /// Click at a specific position in a game window
    /// </summary>
    /// <param name="clientId">Game client identifier</param>
    /// <param name="x">X coordinate (relative to game window)</param>
    /// <param name="y">Y coordinate (relative to game window)</param>
    /// <param name="button">Mouse button to click (left, right, middle)</param>
    /// <returns>Success status and any error message</returns>
    public async Task<(bool Success, string Message)> ClickAsync(string clientId, int x, int y, string button = "left")
    {
        try
        {
            if (!_isWindows)
            {
                return (false, "Click operations are only supported on Windows platform");
            }
            
            _logger.LogInformation("Attempting to click at position ({X}, {Y}) for client {ClientId}", x, y, clientId);
            
            // Find the game window
            var gameWindow = await FindGameWindowAsync(clientId);
            if (gameWindow == IntPtr.Zero)
            {
                return (false, $"Could not find game window for client {clientId}");
            }
            
            // Get window rectangle
            if (!GetWindowRect(gameWindow, out Rectangle windowRect))
            {
                return (false, "Could not get window rectangle");
            }
            
            // Convert relative coordinates to screen coordinates
            var screenX = windowRect.Left + x;
            var screenY = windowRect.Top + y;
            
            // Bring window to foreground
            SetForegroundWindow(gameWindow);
            await Task.Delay(100); // Small delay to ensure window is focused
            
            // Move mouse to target position
            SetCursorPos(screenX, screenY);
            await Task.Delay(50);
            
            // Perform click based on button type
            switch (button.ToLower())
            {
                case "left":
                    mouse_event(MOUSEEVENTF_LEFTDOWN, (uint)screenX, (uint)screenY, 0, 0);
                    await Task.Delay(50);
                    mouse_event(MOUSEEVENTF_LEFTUP, (uint)screenX, (uint)screenY, 0, 0);
                    break;
                    
                case "right":
                    mouse_event(MOUSEEVENTF_RIGHTDOWN, (uint)screenX, (uint)screenY, 0, 0);
                    await Task.Delay(50);
                    mouse_event(MOUSEEVENTF_RIGHTUP, (uint)screenX, (uint)screenY, 0, 0);
                    break;
                    
                default:
                    return (false, $"Unsupported button type: {button}");
            }
            
            _logger.LogInformation("Successfully clicked at ({X}, {Y}) for client {ClientId}", x, y, clientId);
            return (true, $"Successfully clicked at ({x}, {y})");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clicking at position ({X}, {Y}) for client {ClientId}", x, y, clientId);
            return (false, $"Error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Drag from one position to another in the game window
    /// </summary>
    /// <param name="clientId">Game client identifier</param>
    /// <param name="startX">Starting X coordinate</param>
    /// <param name="startY">Starting Y coordinate</param>
    /// <param name="endX">Ending X coordinate</param>
    /// <param name="endY">Ending Y coordinate</param>
    /// <param name="durationMs">Duration of the drag in milliseconds</param>
    /// <returns>Success status and any error message</returns>
    public async Task<(bool Success, string Message)> DragAsync(string clientId, int startX, int startY, int endX, int endY, int durationMs = 500)
    {
        try
        {
            if (!_isWindows)
            {
                return (false, "Drag operations are only supported on Windows platform");
            }
            
            _logger.LogInformation("Attempting to drag from ({StartX}, {StartY}) to ({EndX}, {EndY}) for client {ClientId}", 
                startX, startY, endX, endY, clientId);
            
            var gameWindow = await FindGameWindowAsync(clientId);
            if (gameWindow == IntPtr.Zero)
            {
                return (false, $"Could not find game window for client {clientId}");
            }
            
            if (!GetWindowRect(gameWindow, out Rectangle windowRect))
            {
                return (false, "Could not get window rectangle");
            }
            
            SetForegroundWindow(gameWindow);
            await Task.Delay(100);
            
            // Convert to screen coordinates
            var screenStartX = windowRect.Left + startX;
            var screenStartY = windowRect.Top + startY;
            var screenEndX = windowRect.Left + endX;
            var screenEndY = windowRect.Top + endY;
            
            // Move to start position and press mouse down
            SetCursorPos(screenStartX, screenStartY);
            await Task.Delay(50);
            mouse_event(MOUSEEVENTF_LEFTDOWN, (uint)screenStartX, (uint)screenStartY, 0, 0);
            
            // Calculate steps for smooth dragging
            var steps = Math.Max(1, durationMs / 16); // ~60fps
            var deltaX = (double)(screenEndX - screenStartX) / steps;
            var deltaY = (double)(screenEndY - screenStartY) / steps;
            
            // Perform the drag
            for (int i = 1; i <= steps; i++)
            {
                var currentX = (int)(screenStartX + deltaX * i);
                var currentY = (int)(screenStartY + deltaY * i);
                SetCursorPos(currentX, currentY);
                mouse_event(MOUSEEVENTF_MOVE, (uint)currentX, (uint)currentY, 0, 0);
                await Task.Delay(16); // ~60fps
            }
            
            // Release mouse button
            mouse_event(MOUSEEVENTF_LEFTUP, (uint)screenEndX, (uint)screenEndY, 0, 0);
            
            _logger.LogInformation("Successfully dragged from ({StartX}, {StartY}) to ({EndX}, {EndY}) for client {ClientId}", 
                startX, startY, endX, endY, clientId);
            return (true, $"Successfully dragged from ({startX}, {startY}) to ({endX}, {endY})");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dragging for client {ClientId}", clientId);
            return (false, $"Error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Get the current game state for a client
    /// </summary>
    /// <param name="clientId">Game client identifier</param>
    /// <returns>Game state information</returns>
    public async Task<(bool Success, string Message, GameState? GameState)> GetGameStateAsync(string clientId)
    {
        try
        {
            var gameState = _gameStateManager.GetGameState(clientId);
            if (gameState == null)
            {
                return (false, $"No game state found for client {clientId}", null);
            }
            
            _logger.LogInformation("Retrieved game state for client {ClientId}", clientId);
            return (true, "Game state retrieved successfully", gameState);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving game state for client {ClientId}", clientId);
            return (false, $"Error: {ex.Message}", null);
        }
    }
    
    /// <summary>
    /// Get window information for a game client
    /// </summary>
    /// <param name="clientId">Game client identifier</param>
    /// <returns>Window dimensions and position</returns>
    public async Task<(bool Success, string Message, Rectangle? WindowRect)> GetWindowInfoAsync(string clientId)
    {
        try
        {
            if (!_isWindows)
            {
                return (false, "Window information is only available on Windows platform", null);
            }
            
            var gameWindow = await FindGameWindowAsync(clientId);
            if (gameWindow == IntPtr.Zero)
            {
                return (false, $"Could not find game window for client {clientId}", null);
            }
            
            if (!GetWindowRect(gameWindow, out Rectangle windowRect))
            {
                return (false, "Could not get window rectangle", null);
            }
            
            _logger.LogInformation("Retrieved window info for client {ClientId}: {WindowRect}", clientId, windowRect);
            return (true, "Window info retrieved successfully", windowRect);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving window info for client {ClientId}", clientId);
            return (false, $"Error: {ex.Message}", null);
        }
    }
    
    /// <summary>
    /// List all active game clients
    /// </summary>
    /// <returns>List of active client IDs and their game states</returns>
    public async Task<(bool Success, string Message, Dictionary<string, GameState>? GameStates)> ListActiveClientsAsync()
    {
        try
        {
            var gameStates = _gameStateManager.GetAllGameStates();
            _logger.LogInformation("Retrieved {Count} active game clients", gameStates.Count);
            return (true, $"Found {gameStates.Count} active clients", gameStates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing active clients");
            return (false, $"Error: {ex.Message}", null);
        }
    }
    
    /// <summary>
    /// Simulate keyboard input to the game window
    /// </summary>
    /// <param name="clientId">Game client identifier</param>
    /// <param name="key">Key to press (e.g., "W", "Space", "Enter")</param>
    /// <param name="holdDurationMs">How long to hold the key in milliseconds</param>
    /// <returns>Success status and any error message</returns>
    public async Task<(bool Success, string Message)> SendKeyAsync(string clientId, string key, int holdDurationMs = 50)
    {
        try
        {
            if (!_isWindows)
            {
                return (false, "Keyboard simulation is only supported on Windows platform");
            }
            
            _logger.LogInformation("Attempting to send key '{Key}' to client {ClientId}", key, clientId);
            
            var gameWindow = await FindGameWindowAsync(clientId);
            if (gameWindow == IntPtr.Zero)
            {
                return (false, $"Could not find game window for client {clientId}");
            }
            
            SetForegroundWindow(gameWindow);
            await Task.Delay(100);
            
            // For now, this is a placeholder - full keyboard simulation would require more Windows API calls
            // This could be extended to use SendInput API for more robust keyboard simulation
            
            _logger.LogInformation("Key simulation not fully implemented yet for client {ClientId}", clientId);
            return (true, $"Key '{key}' simulation initiated (placeholder implementation)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending key to client {ClientId}", clientId);
            return (false, $"Error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Find a game window by client ID
    /// This is a simplified implementation - in practice, you might maintain a registry of window handles
    /// </summary>
    /// <param name="clientId">Game client identifier</param>
    /// <returns>Window handle or IntPtr.Zero if not found</returns>
    private async Task<IntPtr> FindGameWindowAsync(string clientId)
    {
        if (!_isWindows)
        {
            return IntPtr.Zero;
        }
        
        // This is a simplified implementation
        // In practice, you would maintain a registry of client IDs to window handles
        // For now, we'll try to find windows with common MonoGame titles
        
        var possibleTitles = new[]
        {
            $"Game1 - {clientId}",
            "SimpleGame",
            "MonoGame",
            clientId
        };
        
        foreach (var title in possibleTitles)
        {
            var handle = FindWindow(null!, title);
            if (handle != IntPtr.Zero)
            {
                return handle;
            }
        }
        
        // If not found by title, get the foreground window as fallback
        // This assumes the game window is currently active
        return GetForegroundWindow();
    }
}
