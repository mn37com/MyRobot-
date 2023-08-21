using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsEngine.Robots
{
    public class FrontRunnerBot : BotPanel
    {
        public FrontRunnerBot(string name, StartProgram startProgram) : base(name, startProgram)
        {
        }

        public override string GetNameStrategyType()
        {
            throw new NotImplementedException();
        }

        public override void ShowIndividualSettingsDialog()
        {
            
        }
    }
}
