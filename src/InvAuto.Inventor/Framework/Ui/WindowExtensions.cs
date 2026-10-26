using System;
using System.Windows;
using System.Windows.Interop;

namespace InvAuto.Framework.Ui
{
    public static class WindowExtensions
    {
        /// <summary>Показывает WPF-окно модально поверх главного окна Inventor.</summary>
        public static bool? ShowDialogOver(this Window window, FeatureContext context)
        {
            new WindowInteropHelper(window).Owner = new IntPtr(context.App.MainFrameHWND);
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.ShowInTaskbar = false;
            return window.ShowDialog();
        }
    }
}
