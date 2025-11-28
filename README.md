# Glass

A transparent Edge WebView2 for displaying the specified website.

## Usage

`Glass.exe <url> [/taskBarIcon] [/sysTrayIcon]`

`url` is the address of the website to display. If the website has a transparent background,
the windows and background behind the window will be visible.

`/taskBarIcon` indicates to the application that a task bar icon should be displayed.

`/sysTrayIcon` indicates to the application that a system tray icon should be display.

Icon and text of both the system tray and on the task bar will be synced from the website.

> NOTE: The application re-launches itself if a task bar icon is requested. This is due to the
fact that some shortcuts for launching the application may override the icon sync behavior
otherwise.