using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace TasbeehTracker
{
    public partial class MainWindow : Window
    {
        private int _count = 0;
        private HwndSource? _hwndSource;
        private bool _hotKeyRegistered = false;
        private const int HOTKEY_ID = 9000;

        // Active hotkey configuration (default is NumPad '+')
        private Key _currentKey = Key.Add;
        private ModifierKeys _currentModifiers = ModifierKeys.None;

        // Global Hotkey P/Invoke
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // DWM (Desktop Window Manager) interop for native shadow shim
        [DllImport("dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled(out bool pfEnabled);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int cxLeftWidth;
            public int cxRightWidth;
            public int cyTopHeight;
            public int cyBottomHeight;
        }

        // Acrylic/blur composition (SetWindowCompositionAttribute for Win10)
        private enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
            ACCENT_INVALID_STATE = 5
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ACCENT_POLICY
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        private enum WINDOWCOMPOSITIONATTRIB
        {
            WCA_ACCENT_POLICY = 19
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWCOMPOSITIONATTRIBDATA
        {
            public WINDOWCOMPOSITIONATTRIB Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

        // Windows 11 native acrylic backdrop API
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int pvAttribute, int cbAttribute);

        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic

        private const int WM_SETTINGCHANGE = 0x001A;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;

            // Hook window messages for setting changes and global hotkey messages
            _hwndSource = HwndSource.FromHwnd(hwnd);
            if (_hwndSource != null)
            {
                _hwndSource.AddHook(HwndHook);
            }

            // Load and apply settings
            UpdateApplicationTheme();
            UpdateApplicationAccentColor();
            LoadHotkeySettings();

            // Register the hotkey
            RegisterActiveHotKey(hwnd);

            // DWM backdrop/acrylic APIs are disabled to allow true WPF alpha transparency (rounded orb only).
            // This prevents Windows from rendering a square blurry card behind the transparent circle.
        }

        private void EnableWindows11Acrylic(IntPtr hwnd)
        {
            try
            {
                int backdropType = DWMSBT_TRANSIENTWINDOW;
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
            }
            catch { /* non-fatal, fallback */ }
        }

        private void EnableBlurOrAcrylic(IntPtr hwnd)
        {
            try
            {
                if (Environment.OSVersion.Version.Major >= 10)
                {
                    ACCENT_POLICY accent = new ACCENT_POLICY
                    {
                        AccentState = (int)AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                        AccentFlags = 2
                    };

                    Color tint = Color.FromArgb(0xB3, 0x00, 0x00, 0x00);
                    if (Application.Current?.Resources["WindowBackgroundBrush"] is SolidColorBrush scb)
                    {
                        tint = scb.Color;
                    }

                    int gradient = (tint.A << 24) | (tint.R << 16) | (tint.G << 8) | tint.B;
                    accent.GradientColor = gradient;

                    int accentStructSize = Marshal.SizeOf(accent);
                    IntPtr accentPtr = Marshal.AllocHGlobal(accentStructSize);
                    try
                    {
                        Marshal.StructureToPtr(accent, accentPtr, false);
                        WINDOWCOMPOSITIONATTRIBDATA data = new WINDOWCOMPOSITIONATTRIBDATA()
                        {
                            Attribute = WINDOWCOMPOSITIONATTRIB.WCA_ACCENT_POLICY,
                            Data = accentPtr,
                            SizeOfData = accentStructSize
                        };

                        SetWindowCompositionAttribute(hwnd, ref data);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(accentPtr);
                    }
                }
            }
            catch { /* non-fatal */ }
        }

        private void EnableDwmShadow(IntPtr hwnd)
        {
            try
            {
                if (Environment.OSVersion.Version.Major >= 6)
                {
                    bool compositionEnabled = false;
                    if (DwmIsCompositionEnabled(out compositionEnabled) == 0 && compositionEnabled)
                    {
                        MARGINS margins = new MARGINS { cxLeftWidth = 1, cxRightWidth = 1, cyTopHeight = 1, cyBottomHeight = 1 };
                        DwmExtendFrameIntoClientArea(hwnd, ref margins);
                    }
                }
            }
            catch { /* non-fatal */ }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY && wParam == new IntPtr(HOTKEY_ID))
            {
                // Increment counter globally
                IncrementCount();
                handled = true;
            }
            else if (msg == WM_SETTINGCHANGE)
            {
                UpdateApplicationTheme();
                UpdateApplicationAccentColor();
            }

            return IntPtr.Zero;
        }

        // --- THEME & ACCENT MANAGEMENT ---

        private void UpdateApplicationTheme()
        {
            if (IsWindowsDarkMode())
            {
                Application.Current.Resources["ForegroundBrush"] = Brushes.White;
                Application.Current.Resources["ButtonBackgroundBrush"] = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
            }
            else
            {
                Application.Current.Resources["ForegroundBrush"] = Brushes.Black;
                Application.Current.Resources["ButtonBackgroundBrush"] = new SolidColorBrush(Color.FromArgb(0x44, 0x00, 0x00, 0x00));
            }
        }

        private void UpdateApplicationAccentColor()
        {
            byte red = 0, green = 191, blue = 255; // Default: DeepSkyBlue (0x00, 0xBF, 0xFF)
            bool gotColor = false;

            // Check if Windows is set to automatically pick an accent color
            bool isAutoColor = false;
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    if (key != null)
                    {
                        object? autoVal = key.GetValue("AutoColorization");
                        if (autoVal != null && Convert.ToInt32(autoVal) == 1)
                        {
                            isAutoColor = true;
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    if (key != null)
                    {
                        // If AutoColorization is enabled, prioritize ColorizationColor (real-time active color)
                        // otherwise prioritize AccentColor (user selected manual color)
                        if (isAutoColor)
                        {
                            object? colVal = key.GetValue("ColorizationColor");
                            if (colVal != null)
                            {
                                uint colorDword = Convert.ToUInt32(colVal);
                                red = (byte)((colorDword >> 16) & 0xFF);
                                green = (byte)((colorDword >> 8) & 0xFF);
                                blue = (byte)(colorDword & 0xFF);
                                gotColor = true;
                            }
                        }
                        
                        if (!gotColor)
                        {
                            object? val = key.GetValue("AccentColor");
                            if (val != null)
                            {
                                uint accentColorDword = Convert.ToUInt32(val);
                                blue = (byte)((accentColorDword >> 16) & 0xFF);
                                green = (byte)((accentColorDword >> 8) & 0xFF);
                                red = (byte)(accentColorDword & 0xFF);
                                gotColor = true;
                            }
                        }

                        if (!gotColor && !isAutoColor)
                        {
                            object? colVal = key.GetValue("ColorizationColor");
                            if (colVal != null)
                            {
                                uint colorDword = Convert.ToUInt32(colVal);
                                red = (byte)((colorDword >> 16) & 0xFF);
                                green = (byte)((colorDword >> 8) & 0xFF);
                                blue = (byte)(colorDword & 0xFF);
                                gotColor = true;
                            }
                        }
                    }
                }
            }
            catch { }

            if (!gotColor)
            {
                try
                {
                    Color glassColor = SystemParameters.WindowGlassColor;
                    red = glassColor.R;
                    green = glassColor.G;
                    blue = glassColor.B;
                }
                catch { }
            }

            // Apply dynamic colors to the border and glow effects directly
            Color opaqueAccentColor = Color.FromRgb(red, green, blue);
            Application.Current.Resources["WindowBorderBrush"] = new SolidColorBrush(opaqueAccentColor);

            if (OrbBorder != null)
            {
                OrbBorder.BorderBrush = new SolidColorBrush(opaqueAccentColor);
            }
            if (OrbGlow != null)
            {
                OrbGlow.Color = opaqueAccentColor;
            }

            // Define translucent backgrounds tinted for readability based on theme (Rich Aesthetics)
            Color bgAccentColor;
            if (IsWindowsDarkMode())
            {
                // Dark mode tint: Blend accent color with black (30% accent, 70% black) at 17% opacity (0x2B)
                byte darkR = (byte)(red * 0.3);
                byte darkG = (byte)(green * 0.3);
                byte darkB = (byte)(blue * 0.3);
                bgAccentColor = Color.FromArgb(0x2B, darkR, darkG, darkB);
            }
            else
            {
                // Light mode tint: Blend accent color with white (30% accent, 70% white) at 17% opacity (0x2B)
                byte lightR = (byte)(255 - (255 - red) * 0.3);
                byte lightG = (byte)(255 - (255 - green) * 0.3);
                byte lightB = (byte)(255 - (255 - blue) * 0.3);
                bgAccentColor = Color.FromArgb(0x2B, lightR, lightG, lightB);
            }

            Application.Current.Resources["WindowBackgroundBrush"] = new SolidColorBrush(bgAccentColor);
        }

        private bool IsWindowsDarkMode()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object? val = key.GetValue("SystemUsesLightTheme");
                        if (val != null && Convert.ToInt32(val) == 0) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // --- HOTKEY PERSISTENCE & REGISTRATION ---

        private void RegisterActiveHotKey(IntPtr hwnd)
        {
            try
            {
                int vk = KeyInterop.VirtualKeyFromKey(_currentKey);
                if (vk == 0) return;

                uint fsModifiers = 0;
                if (_currentModifiers.HasFlag(ModifierKeys.Alt)) fsModifiers |= 0x0001;
                if (_currentModifiers.HasFlag(ModifierKeys.Control)) fsModifiers |= 0x0002;
                if (_currentModifiers.HasFlag(ModifierKeys.Shift)) fsModifiers |= 0x0004;
                if (_currentModifiers.HasFlag(ModifierKeys.Windows)) fsModifiers |= 0x0008;

                _hotKeyRegistered = RegisterHotKey(hwnd, HOTKEY_ID, fsModifiers, (uint)vk);
            }
            catch
            {
                _hotKeyRegistered = false;
            }
        }

        private bool TryRegisterNewHotKey(Key key, ModifierKeys modifiers)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;

            // Unregister current hotkey
            if (_hotKeyRegistered)
            {
                try { UnregisterHotKey(hwnd, HOTKEY_ID); } catch { }
                _hotKeyRegistered = false;
            }

            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk == 0) return false;

            uint fsModifiers = 0;
            if (modifiers.HasFlag(ModifierKeys.Alt)) fsModifiers |= 0x0001;
            if (modifiers.HasFlag(ModifierKeys.Control)) fsModifiers |= 0x0002;
            if (modifiers.HasFlag(ModifierKeys.Shift)) fsModifiers |= 0x0004;
            if (modifiers.HasFlag(ModifierKeys.Windows)) fsModifiers |= 0x0008;

            try
            {
                _hotKeyRegistered = RegisterHotKey(hwnd, HOTKEY_ID, fsModifiers, (uint)vk);
                if (_hotKeyRegistered)
                {
                    _currentKey = key;
                    _currentModifiers = modifiers;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private void SaveHotkeySettings(Key key, ModifierKeys modifiers)
        {
            try
            {
                using (RegistryKey? subKey = Registry.CurrentUser.CreateSubKey(@"Software\TasbeehTracker"))
                {
                    if (subKey != null)
                    {
                        subKey.SetValue("HotkeyKey", (int)key);
                        subKey.SetValue("HotkeyModifiers", (int)modifiers);
                    }
                }
            }
            catch { }
        }

        private void LoadHotkeySettings()
        {
            try
            {
                using (RegistryKey? subKey = Registry.CurrentUser.OpenSubKey(@"Software\TasbeehTracker"))
                {
                    if (subKey != null)
                    {
                        object? keyVal = subKey.GetValue("HotkeyKey");
                        object? modVal = subKey.GetValue("HotkeyModifiers");
                        if (keyVal != null && modVal != null)
                        {
                            _currentKey = (Key)Convert.ToInt32(keyVal);
                            _currentModifiers = (ModifierKeys)Convert.ToInt32(modVal);
                        }
                    }
                }
            }
            catch { }
        }

        private string GetHotkeyString(Key key, ModifierKeys modifiers)
        {
            var sb = new System.Text.StringBuilder();
            if (modifiers.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
            if (modifiers.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
            if (modifiers.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
            if (modifiers.HasFlag(ModifierKeys.Windows)) sb.Append("Win+");
            sb.Append(key.ToString());
            return sb.ToString();
        }

        // --- MATH & ANIMATION HELPERS ---

        private void IncrementCount()
        {
            _count++;
            CountText.Text = _count.ToString();

            // Execute bounce & glow storyboard animation
            if (TryFindResource("IncrementAnimation") is Storyboard sb)
            {
                sb.Begin();
            }
        }



        // --- UI INTERACTION HANDLERS ---

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (SettingsGrid.Visibility == Visibility.Visible)
            {
                e.Handled = true;

                Key key = e.Key == Key.System ? e.SystemKey : e.Key;

                // Ignore modifier keys alone
                if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                    key == Key.LeftShift || key == Key.RightShift ||
                    key == Key.LeftAlt || key == Key.RightAlt ||
                    key == Key.LWin || key == Key.RWin)
                {
                    return;
                }

                ModifierKeys modifiers = Keyboard.Modifiers;

                // Escape cancels
                if (key == Key.Escape && modifiers == ModifierKeys.None)
                {
                    SettingsGrid.Visibility = Visibility.Collapsed;
                    RootGrid.IsEnabled = true;
                    return;
                }

                if (TryRegisterNewHotKey(key, modifiers))
                {
                    SaveHotkeySettings(key, modifiers);
                    SettingsStatusText.Text = $"Registered:\n{GetHotkeyString(key, modifiers)}";

                    // Go back after displaying success message
                    var timer = new System.Windows.Threading.DispatcherTimer();
                    timer.Interval = TimeSpan.FromSeconds(1.5);
                    timer.Tick += (s, args) =>
                    {
                        SettingsGrid.Visibility = Visibility.Collapsed;
                        RootGrid.IsEnabled = true;
                        timer.Stop();
                    };
                    timer.Start();
                }
                else
                {
                    SettingsStatusText.Text = "Failed to register!\nTry another key.";
                }
            }
        }

        private void ChangeHotkey_Click(object sender, RoutedEventArgs e)
        {
            SettingsStatusText.Text = $"Current Hotkey:\n{GetHotkeyString(_currentKey, _currentModifiers)}\n\nPress new hotkey...";
            SettingsGrid.Visibility = Visibility.Visible;
        }

        private void CancelSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsGrid.Visibility = Visibility.Collapsed;
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _count = 0;
            CountText.Text = "0";
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (_hwndSource != null)
            {
                try { _hwndSource.RemoveHook(HwndHook); } catch { }
                _hwndSource = null;
            }

            if (_hotKeyRegistered)
            {
                try { UnregisterHotKey(hwnd, HOTKEY_ID); } catch { }
                _hotKeyRegistered = false;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}