using System.ComponentModel;

namespace OsEngine.Robots.FrontRunner_2.ViewModels
{
    public class BaseVM : INotifyPropertyChanged
    {

        public void OnPropertyChanged(string prop = "")
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(prop));
        }

        //========================== Events ===================================

        public event PropertyChangedEventHandler PropertyChanged;

    }
}