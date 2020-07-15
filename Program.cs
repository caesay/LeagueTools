using Hardcodet.Wpf.TaskbarNotification;
using Ookii.Dialogs.Wpf;
using PromptLibrary;
using Swan;
using Swan.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LeagueTools
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            new App().Run();
        }
    }
}
