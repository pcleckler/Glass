using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Data.SqlTypes;
using System.Diagnostics;
using Timer = System.Windows.Forms.Timer;

namespace Glass
{
    public partial class MainWindow : Form
    {
        const string TaskBarIconSwitch = "/taskbarIcon";
        const string SysTrayIconSwitch = "/sysTrayIcon";
        const string RelaunchedSwitch = "/relaunched";

        WebView2 webView;
        WebView2FrameSync iconSync;
        Timer refreshTimer;
        string programFilename = string.Empty;
        string url = string.Empty;
        Boolean closeRequested = false;

        public MainWindow()
        {
            InitializeComponent();

            this.webView = new WebView2();

            this.webView.BackColor = this.BackColor;
            this.webView.DefaultBackgroundColor = Color.Transparent;

            this.webView.NavigationCompleted += this.WebView_NavigationCompleted;

            this.Controls.Add(this.webView);

            this.BackColor = Color.Red; // Only color that appears to support this behavior?

            this.TransparencyKey = this.BackColor;

            this.iconSync = new WebView2FrameSync(this.webView, this);

            this.refreshTimer = new Timer();
            this.refreshTimer.Interval = 2000;
            this.refreshTimer.Tick += this.RefreshTimer_Tick;

            // Evaluate command line options
            var cmdlineArgs = Environment.GetCommandLineArgs();

            this.programFilename = Path.GetFileName(cmdlineArgs[0]);

            var relaunched = false;

            if (cmdlineArgs.Length > 1)
            {
                foreach (var arg in cmdlineArgs)
                {
                    var lowerArg = arg.ToLower().Trim();

                    if (lowerArg == TaskBarIconSwitch.ToLower())
                    {
                        this.ShowInTaskbar = true;
                    }

                    if (lowerArg == SysTrayIconSwitch.ToLower())
                    {
                        this.iconSync.ShowSysTrayIcon = true;
                    }

                    if (lowerArg == RelaunchedSwitch.ToLower())
                    {
                        relaunched = true;
                    }
                }

                this.url = cmdlineArgs[1];

                bool isValid = Uri.TryCreate(this.url, UriKind.Absolute, out Uri? uriResult)
                       && (uriResult.Scheme == Uri.UriSchemeHttp ||
                           uriResult.Scheme == Uri.UriSchemeHttps ||
                           uriResult.Scheme == Uri.UriSchemeFile);

                if (!isValid)
                {
                    this.Usage();
                    this.closeRequested = true;
                    return;
                }

                // Shortcuts can override the icon. If taskbar icon requested, relaunch.
                if (!relaunched && this.ShowInTaskbar)
                {
                    var args = new List<string>
                    {
                        $"\"{this.url}\"",
                        TaskBarIconSwitch,
                        RelaunchedSwitch,
                        (this.iconSync.ShowSysTrayIcon ? SysTrayIconSwitch : "")                        
                    };

                    var filename = this.programFilename;

                    // A DLL cannot be used to start a process by itself. Launch the EXE.
                    if (Path.GetExtension(filename).ToLower() == ".dll")
                    {
                        filename = Path.Combine(
                            Path.GetDirectoryName(filename) ?? "", 
                            $"{Path.GetFileNameWithoutExtension(filename)}.exe");
                    }

                    Process.Start(filename, string.Join(" ", args));
                    this.closeRequested = true;
                    return;
                }

                // Begin processing
                this.iconSync.Enable();
                this.refreshTimer.Enabled = true;
            }
        }

        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            this.iconSync.Update();
        }

        private void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {

        }

        private void Usage()
        {
            MessageBox.Show(
                $"Usage: {this.programFilename} <url> [{TaskBarIconSwitch}] [{SysTrayIconSwitch}]\n\nwhere 'url' is the address of the web site or HTML file to load, '{TaskBarIconSwitch}' should be displayed, and '{SysTrayIconSwitch}' indicates that a system tray icon should be provided.", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private async void MainWindow_Load(object sender, EventArgs e)
        {
            if (this.closeRequested)
            {
                this.Close();
                return;
            }

            await this.webView.EnsureCoreWebView2Async(null);

            this.webView.CoreWebView2.Navigate(this.url);
        }

        private void MainWindow_Activated(object sender, EventArgs e)
        {
            var size = this.Size;
            var location = this.Location;

            this.SuspendLayout();
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.Size = size;
            this.Location = location;
            this.RedockWebView();
            this.ResumeLayout(true);
        }

        private void MainWindow_Deactivate(object sender, EventArgs e)
        {
            var size = this.Size;
            var location = this.Location;

            this.SuspendLayout();
            this.FormBorderStyle = FormBorderStyle.None;
            this.Size = size;
            this.Location = location;
            this.RedockWebView();
            this.ResumeLayout(true);
        }

        private void RedockWebView()
        {
            this.webView.Dock = DockStyle.None;
            this.webView.Dock = DockStyle.Fill;
            this.webView.Invalidate();
        }
    }
}
