using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;

namespace Simple_Doomsday_Engine_Launcher.ViewModels
{
    public class LogWindowViewModel : ViewModelBase
    {
        public ObservableCollection<string> LogEntries { get; } = new();

        public void Add(string message)
        {
            LogEntries.Add(message);
        }


    }


}
