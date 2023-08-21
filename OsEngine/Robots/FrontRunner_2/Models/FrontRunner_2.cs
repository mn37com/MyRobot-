using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsEngine.Robots.FrontRunner_2.Models
{
    public class FrontRunner_2Bot : BotPanel
    {
        public FrontRunner_2Bot(string name, StartProgram startProgram) : base(name, startProgram)
        {
        }

        #region Fields =================================================================================
        public decimal BigVolume;
        public decimal Offset;
        public decimal Take;
        public decimal Lot;
        public Position Position;

        
        #endregion
        
            #region Methods =================================================================================
        public override string GetNameStrategyType()
        {
            return "FrontRunner_2Bot";
        }

        public override void ShowIndividualSettingsDialog()
        {
            
        }
        #endregion


    }
}
