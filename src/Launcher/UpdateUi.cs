using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace HyruleOnline {
 sealed partial class Launcher {
  UpdateInfo update;
  bool updating;

  // Checked once, when the window opens, and only for installs made by the setup (the uninstaller marks those).
  void StartUpdateCheck() {
   Updates.DropBackup(root);
   string note = Updates.Failure(root);
   if (File.Exists(note)) {
    string reason = ""; try { reason = File.ReadAllText(note).Trim(); File.Delete(note); } catch (Exception) { }
    Write("The last update failed and the previous version was restored: " + reason);
    SetStatus("The last update failed and was undone.");
   }
   string current = null;
   try { current = File.ReadAllText(Path.Combine(root, "VERSION")).Trim(); } catch (Exception) { }
   if (current == null || !File.Exists(Path.Combine(root, "unins000.exe"))) return;
   ThreadPool.QueueUserWorkItem(state => {
    var found = Updates.Find(current);
    if (found != null) Window.Dispatcher.BeginInvoke(new Action(() => {
     update = found;
     var button = C<Button>("Update");
     button.Content = Tr("Update to {0}", found.Version);
     button.Visibility = Visibility.Visible;
     button.Click += (s, e) => ApplyUpdate();
    }));
   });
  }

  bool SessionBusy() { return script != null || sessionLive || gameRunning || installing || Scripts.Owned(root).Count > 0; }

  void ApplyUpdate() {
   if (updating || update == null) return;
   if (SessionBusy()) { SetStatus("Stop the session first."); return; }
   updating = true;
   var button = C<Button>("Update");
   var info = update;
   button.IsEnabled = false;
   button.Content = Tr("Downloading {0}%", 0);
   ThreadPool.QueueUserWorkItem(state => {
    string setup = null, failure = null;
    try {
     setup = Updates.Fetch(info, percent => Window.Dispatcher.BeginInvoke(new Action(() => button.Content = Tr("Downloading {0}%", percent))));
     Window.Dispatcher.BeginInvoke(new Action(() => button.Content = Tr("Installing...")));
     Updates.MakeBackup(root, AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'));
    }
    catch (Exception error) { failure = error.Message; }
    Window.Dispatcher.BeginInvoke(new Action(() => {
     // A session may have been started while the installer downloaded.
     if (failure == null && SessionBusy()) failure = Tr("Stop the session first.");
     if (failure == null) {
      try { Updates.Handover(root, Assembly.GetExecutingAssembly().Location, setup, info.Version); Window.Close(); return; }
      catch (Exception error) { failure = error.Message; }
     }
     Updates.DropBackup(root);
     updating = false;
     button.IsEnabled = true;
     button.Content = Tr("Update to {0}", info.Version);
     SetStatus("Update failed: {0}", failure);
    }));
   });
  }
 }
}