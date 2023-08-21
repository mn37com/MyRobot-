using OsEngine.Robots.FrontRunner.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OsEngine.Robots.FrontRunner_2.Models;
using System.Windows.Input;

namespace OsEngine.Robots.FrontRunner_2.ViewModels
{
    public class VM : BaseVM
    {
        public VM(FrontRunner_2Bot bot)
        {
            _bot = bot;
        }
        #region Fields ==============================================================
        private FrontRunner_2Bot _bot;
        #endregion

        #region Properties ==============================================================
        public decimal BigVolume
        {
            get => _bot.BigVolume;
            
            set
            { _bot.BigVolume = value;
                OnPropertyChanged(nameof(BigVolume));
            }
        }
        

        public decimal Offset
        {
            get => _bot.Offset;

            set
            {
                _bot.Offset = value;
                OnPropertyChanged(nameof(Offset));
            }
        }


        public decimal Take
        {
            get => _bot.Take;

            set
            {
                _bot.Take = value;
                OnPropertyChanged(nameof(Take));
            }
        }
       
        public decimal Lot
        {
            get => _bot.Lot;

            set
            {
                _bot.Lot = value;
                OnPropertyChanged(nameof(Lot));
            }
        }
  
        public Edit Edit
        {
            get => _edit;

            set
            {
                _edit = value;
                OnPropertyChanged(nameof(Edit));
            }
        }
        public Edit _edit;


        #endregion

        #region Commands ==============================================================
        private DelegateCommand commandStart;
        public ICommand CommandStart
        {
            get
            {
                if (commandStart == null)
                {
                    commandStart = new DelegateCommand(Start);
                }
                return commandStart;
            }
        }
        #endregion

        #region Methods ==============================================================
        private void Start(object obj)
        {
            if (Edit == Edit.Start)
            {
                Edit = Edit.Stop;
            }
            else
            {
                Edit = Edit.Start;
            }
        }
        #endregion
    }
    public enum Edit
    { 
    Start,
    Stop
    }
}
