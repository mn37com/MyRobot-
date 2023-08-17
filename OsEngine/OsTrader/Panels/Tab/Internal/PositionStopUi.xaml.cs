﻿/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Globalization;
using System.Windows;
using OsEngine.Entity;
using OsEngine.Language;

namespace OsEngine.OsTrader.Panels.Tab.Internal
{
    public partial class PositionStopUi
    {
        public PositionStopUi(Position position, decimal lastSecurityPrice, string title)
        {
            InitializeComponent();
            OsEngine.Layout.StickyBorders.Listen(this);
            OsEngine.Layout.StartupLocation.Start_MouseInCentre(this);
            Title = title;

            TextBoxPriceOrder.Text = lastSecurityPrice.ToStringWithNoEndZero();
            TextBoxPriceActivation.Text = lastSecurityPrice.ToStringWithNoEndZero();
            TextBoxSecurity.Text = position.OpenOrders[0].SecurityNameCode;
            TextBoxPositionNumber.Text = position.Number.ToString(new CultureInfo("ru-RU"));
            TextBoxVolume.Text = position.OpenVolume.ToStringWithNoEndZero();

            LabelPositionNumber.Content = OsLocalization.Trader.Label101;
            LabelSecurity.Content = OsLocalization.Trader.Label102;
            LabelVolume.Content = OsLocalization.Trader.Label30;
            ButtonAccept.Content = OsLocalization.Trader.Label17;
            LabelActivationPrice.Content = OsLocalization.Trader.Label108;
            LabelOrderPrice.Content = OsLocalization.Trader.Label109;

            this.Activate();
            this.Focus();
        }

        public bool IsAccept;

        public decimal PriceOrder;

        public decimal PriceActivate;

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PriceOrder = TextBoxPriceOrder.Text.ToDecimal();
                PriceActivate = TextBoxPriceActivation.Text.ToDecimal();
                IsAccept = true;
            }
            catch
            {
                MessageBox.Show(OsLocalization.Trader.Label13);
                return;
            }
            Close();
        }
    }
}
