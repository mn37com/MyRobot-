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
            //_tab.PositionOpeningSuccesEvent += _tab_PositionOpeningSuccesEvent;
            _tab.PositionOpeningFailEvent += _tab_PositionOpeningFailEvent;
        }


        #region Fields =================================================================================
        public decimal BigVolume=20000;
        public int Offset=1;
        public int Take=5;
        public decimal Lot=2;
        public Position Position = null;
        private BotTabSimple _tab;
        // public Edit Edit = ViewModels.Edit.Stop;
        // private bool _stateBid = true;
        // private bool _stateAsk = true;
        #endregion

        #region Properties =================================================================================

        public Edit Edit 
        {
            get => _edit;

            set {
                _edit=value;
                if (_edit==Edit.Stop &&
                    Position != null
                && Position.State ==PositionStateType.Opening)
                {
                    _tab.CloseAllOrderInSystem();
                }
            }
        }
        Edit _edit = ViewModels.Edit.Stop;
        #endregion

        #region Methods =================================================================================
        private void _tab_PositionOpeningFailEvent(Position pos)
        {
             Position = null;
        }
      //  private void _tab_PositionOpeningSuccesEvent(Position pos)
      //  {
            //Position = pos;
            //_tab.CloseAllOrderInSystem();
       /*     if (Position.Direction==Side.Sell) 
            {
                decimal takePrice = Position.EntryPrice + _tab.Securiti.PriceStep;
                _tab.CloseAtProfit(Position, takePrice,takePrice);
            }
            else if (Position.Direction==Side.Buy)
            {
                decimal takePrice = Position.EntryPrice + _tab.Securiti.PriceStep;
                _tab.CloseAtProfit(Position, takePrice, takePrice);
            }
       */
       //  }
        private void _tab_MarketDepthUpdateEvent(MarketDepth marketDepth)
        {
            if (Edit == Edit.Stop) 
            { 
                return; 
            }

          if (marketDepth.SecurityNameCode != _tab.Securiti.Name)
            {
                return;
             }
            List <Position> positions=_tab.PositionsOpenAll;
            if (positions != null
                && positions.Count > 0) 
            {
                foreach (Position pos in positions)
                {
                    if (Position.Direction == Side.Sell)
                    {
                        decimal takePrice = Position.EntryPrice + _tab.Securiti.PriceStep;
                        _tab.CloseAtProfit(Position, takePrice, takePrice);
                    }
                    else if (Position.Direction == Side.Buy)
                    {
                        decimal takePrice = Position.EntryPrice + _tab.Securiti.PriceStep;
                        _tab.CloseAtProfit(Position, takePrice, takePrice);
                    }
                }
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
                    if (Position.State!=PositionStateType.Open
                        && Position.State != PositionStateType.Opening) 
                    { 
                        Position = null;
                    }
                }
                if (Position != null
                         && marketDepth.Bids[i].Price == Position.EntryPrice
                         && marketDepth.Bids[i].Bid > BigVolume / 2)
                {
                    if (Position.State==PositionStateType.Open)
                    {
                        _tab.CloseAtMarket(Position, Position.OpenVolume);
                    }

                    else if (Position.State==PositionStateType.Opening)
                    {
                        _tab.CloseAllOrderInSystem();
                    }
                }
                else if (Position!=null
                    && marketDepth.Bids[i].Bid > BigVolume
                    && marketDepth.Bids[i].Price >Position.EntryPrice- Offset * _tab.Securiti.PriceStep)   
                {
                    _tab.CloseAllOrderInSystem(); 
                    Position = null;    
                    break;
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
