using System;
using System.Windows.Forms;
using apca.Forms;

namespace apca
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}