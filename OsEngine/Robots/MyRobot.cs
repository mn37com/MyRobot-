using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Animation;

namespace OsEngine.Robots
{
    internal class MyRobot : BotPanel
    {
        public MyRobot(string name, StartProgram startProgram) : base(name, startProgram)
        {
            this.TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];
            CreateParameter("Mode", "Edit", new[] { "Edit", "Trade" });
            _risk = CreateParameter("Risk %", 1m, 0.1m, 10m, 0.1m);
            _profitKoef = CreateParameter("Koef Profit", 1m, 0.1m, 10m, 0.1m);
            _countDownCandles = CreateParameter("Count down candles", 1, 1, 5, 1);
            _koefVolume = CreateParameter("Koef volume", 2m, 2m, 10m, 0.5m);
            _countCandles = CreateParameter("Count Candles", 10, 5, 50, 1);
            _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
            _tab.PositionOpeningSuccesEvent += _tab_PositionOpeningSuccesEvent;
        }


        public override string GetNameStrategyType()
        {
            return nameof(MyRobot);
        }

        public override void ShowIndividualSettingsDialog()
        {
            WindowMyRobot window = new WindowMyRobot();
            StrategyParameterString parameterString = (StrategyParameterString)Parameters[0];
            StrategyParameterInt parameterInt = (StrategyParameterInt)Parameters[1];
            // window.TextRobot.Text = parameterString.ValueString + " = " + parameterInt.ValueInt;
            window.TextRobot.Text = "Lot = " + parameterInt.ValueInt;
            window.ShowDialog();
        }
        #region ================================Fields =============================================
        private BotTabSimple _tab;

        /// <summary>
        /// Риск на сделку 
        /// </summary>
        private StrategyParameterDecimal _risk;

        /// <summary>
        /// Во сколько раз тейк больше риска 
        /// </summary>
        private StrategyParameterDecimal _profitKoef;

        /// <summary>
        /// Количество падающих свечей перед объемным разворотом
        /// </summary>
        private StrategyParameterInt _countDownCandles;

        /// <summary>
        /// Во сколько раз объем превышает средний 
        /// </summary>
        private StrategyParameterDecimal _koefVolume;

        /// <summary>
        /// Средний объем
        /// </summary>
        private decimal _averageVolume;

        /// <summary>
        /// количество пунктов до стоп лосса
        /// </summary>
        private int _punkts = 0;

        private decimal _lowCandle = 0;

        /// <summary>
        /// количество свечей для вычисления среднего объема
        /// </summary>
        private StrategyParameterInt _countCandles;
        #endregion


        #region ================================Methods =============================================
        private void _tab_CandleFinishedEvent(List<Candle> candles)
        {
          //  SaveCSVBar(candles);
            if (candles.Count < _countDownCandles.ValueInt + 1
                || candles.Count < _countCandles.ValueInt + 1)
            { return; }

            _averageVolume = 0;

            for (int i = candles.Count - 2; i > candles.Count - _countCandles.ValueInt - 2; i--)
            {
                _averageVolume += candles[i].Volume;
            }
            _averageVolume /= _countCandles.ValueInt;

            // ограничение, чтобы не заходил в сделки при открытой позиции

            List<Position> positions = _tab.PositionOpenLong;
            if (positions.Count > 0)
            { return; }

            Candle candle = candles[candles.Count - 1];
            if (candle.Close < (candle.High + candle.Low) / 2 || candle.Volume < _averageVolume * _koefVolume.ValueDecimal)
            { return; }

            for (int i = candles.Count - 2; i > candles.Count - 2 - _countDownCandles.ValueInt; i--)
            {
                if (candles[i].Close > candles[i].Open)
                { return; }
            }

            _punkts = (int)((candle.Close - candle.Low) / _tab.Securiti.PriceStep);

            if (_punkts < 5)
            { return; }
            decimal amountStop = _punkts * _tab.Securiti.PriceStepCost;
            decimal amountRisk = _tab.Portfolio.ValueBegin * _risk.ValueDecimal / 100;
            decimal volume = Math.Round(amountRisk / amountStop);
            decimal go = 10000;


            if (_tab.Securiti.Go > 1)
            {
                go = _tab.Securiti.Go;
            }

            decimal maxLot = _tab.Portfolio.ValueBegin / go;
            _lowCandle = candle.Low;
            if (volume < maxLot)
            { _tab.BuyAtMarket(volume); }

            //_lowCandle = candle.Low;
            //_tab.BuyAtMarket(volume);
        }

        private void _tab_PositionOpeningSuccesEvent(Position pos)
        {
            decimal priceTake = pos.EntryPrice + _punkts * _profitKoef.ValueDecimal;
            _tab.CloseAtProfit(pos, priceTake, priceTake);
            _tab.CloseAtStop(pos, _lowCandle, _lowCandle - 100 * _tab.Securiti.PriceStep);
           // SaveCSV(pos);
        }
/*
        private void SaveCSV(Position pos)
        {
            //@"Engine\trades.csv"  
            if (!File.Exists("C:\\_1\\trades.csv"))
            {
                string header = "Позиция";
                using (StreamWriter writer = new StreamWriter("C:\\_1\\trades.csv", false))
                {
                    writer.WriteLine(header);
                    writer.Close();
                }
            }


            using (StreamWriter writer = new StreamWriter("C:\\_1\\trades.csv", true))
            {
                string str = ";;;;;;;;;" + pos.TimeOpen.ToShortDateString();
                str += ";" + pos.TimeOpen.TimeOfDay;
                writer.WriteLine(str);
                writer.Close();
            }

        }

        private void SaveCSVBar(List<Candle> candles)
        {
            //@"Engine\trades.csv"  
            if (!File.Exists("C:\\_1\\candle.csv"))
            {
                string header = "Бары";
                using (StreamWriter writer = new StreamWriter("C:\\_1\\candle.csv", false))
                {
                    writer.WriteLine(header);
                    writer.Close();
                }
            }

            for (int i = 0; i <candles.Count; i++)
                using (StreamWriter writer = new StreamWriter("C:\\_1\\candle.csv", true))
            {
                    string str = "";
                    str += ((int)candles[i].Open).ToString();
                    str += ";" + ((int)candles[i].High).ToString();
                    str += ";" + ((int)candles[i].Low).ToString();
                    str += ";" + ((int)candles[i].Close).ToString();
                    str += ";" + candles[i].TimeStart.ToString();
                    writer.WriteLine(str);
                writer.Close();
            }
        }
*/

        #endregion
    }
}

