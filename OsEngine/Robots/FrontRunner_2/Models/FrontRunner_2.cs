using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.Robots.FrontRunner_2.ViewModels;
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
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];
            _tab.MarketDepthUpdateEvent += _tab_MarketDepthUpdateEvent;
            _tab.PositionOpeningSuccesEvent += _tab_PositionOpeningSuccesEvent;
        }

        #region Fields =================================================================================
        public decimal BigVolume;
        public int Offset;
        public int Take;
        public decimal Lot;
        public Position Position = null;
        private BotTabSimple _tab;
        public Edit Edit = ViewModels.Edit.Stop;
       // private bool _stateBid = true;
       // private bool _stateAsk = true;
        #endregion

        #region Methods =================================================================================
        private void _tab_PositionOpeningSuccesEvent(Position pos)
        {
            Position = pos;
            _tab.CloseAllOrderInSystem();
            if (Position.Direction==Side.Sell) 
            {
                decimal takePrice = Position.EntryPrice + _tab.Securiti.PriceStep;
                _tab.CloseAtProfit(Position, takePrice,takePrice);
            }
            else if (Position.Direction==Side.Buy)
            {
                decimal takePrice = Position.EntryPrice + _tab.Securiti.PriceStep;
                _tab.CloseAtProfit(Position, takePrice, takePrice);
            }
         }
        private void _tab_MarketDepthUpdateEvent(MarketDepth marketDepth)
        {
          if (marketDepth.SecurityNameCode != _tab.Securiti.Name)
            {
                return;
             }
            for (int i=0; i<marketDepth.Asks.Count; i++)
            {
                if (marketDepth.Asks[i].Ask>=BigVolume && Position==null )
                {
                    decimal price = marketDepth.Asks[i].Price - Take * _tab.Securiti.PriceStep;
                   // _stateAsk = false;
                   // _tab.SellAtLimit(Lot, price);
                }
                if (Position!=null 
                    && marketDepth.Asks[i].Price == Position.EntryPrice
                    && marketDepth.Asks[i].Ask < BigVolume/2)
                {
                   // _tab.CloseAtMarket(Position, Position.OpenVolume);
                }

            }
            for (int i = 0; i < marketDepth.Bids.Count; i++)
            {

                // if (marketDepth.Bids[i].Bid >= BigVolume && Position == null && _stateBid)
                if (marketDepth.Bids[i].Bid >= BigVolume && Position == null)
                {
                    decimal price = marketDepth.Bids[i].Price + Offset * _tab.Securiti.PriceStep;
                    //_stateBid= false;
                    Position =  _tab.BuyAtLimit(Lot, price);
                }
                if (Position != null
                         && marketDepth.Bids[i].Price == Position.EntryPrice
                         && marketDepth.Bids[i].Bid > BigVolume / 2)
                {
                    _tab.CloseAtMarket(Position, Position.OpenVolume);
                }
            }

        }

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
