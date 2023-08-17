﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OsEngine.Entity;
using OsEngine.Language;
using OsEngine.Logging;
using OsEngine.Market.Servers.Entity;
using OsEngine.Market.Servers.GateIo.Futures.Entities;
using OsEngine.Market.Servers.GateIo.Futures.Request;
using OsEngine.Market.Servers.GateIo.Futures.Response;
using OsEngine.Market.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace OsEngine.Market.Servers.GateIo.Futures
{
    public class GateIoFuturesServer : AServer
    {
        public GateIoFuturesServer()
        {
            GateIoFuturesServerRealization realization = new GateIoFuturesServerRealization();
            ServerRealization = realization;

            CreateParameterString(OsLocalization.Market.ServerParamPublicKey, "");
            CreateParameterPassword(OsLocalization.Market.ServerParamSecretKey, "");
            CreateParameterString("User ID", "");
            CreateParameterEnum("Base Wallet", "USDT", new List<string> { "USDT", "BTC" });
            CreateParameterEnum("Position Mode", "Single", new List<string> { "Single", "Double" });

        }

        /// <summary>
        /// instrument history query
        /// запрос истории по инструменту
        /// </summary>
        public List<Candle> GetCandleHistory(string nameSec, TimeSpan tf)
        {
            return ((GateIoFuturesServerRealization)ServerRealization).GetCandleHistory(nameSec, tf);
        }
    }

    public class GateIoFuturesServerRealization : IServerRealization
    {
        private string _publicKey;
        private string _secretKey;

        private RestRequestBuilder _requestREST;
        private WsSource _wsSource;
        private Signer _signer;
        public List<Portfolio> Portfolios;
        private readonly GfMarketDepthCreator _mDepthCreator;
        private readonly GfOrderCreator _orderCreator;
        private DateTime _lastTimeUpdateSocket;


        private string _host = "https://fx-api-testnet.gateio.ws";
        private string _path = "/api/v4/futures";
        private string _wallet = "/usdt";
        private string _baseUrlWss = "wss://fx-ws-testnet.gateio.ws/v4/ws";
        private string _postionMode = "Double";
        private string _userId = "";
        private string _baseWallet = "";
        private const string PortfolioNumber = "GateIoFutures";

        /// <summary>
        /// словарь таймфреймов, поддерживаемых этой биржей
        /// </summary>
        private readonly Dictionary<int, string> _supportedIntervals;

        public GateIoFuturesServerRealization()
        {
            _supportedIntervals = CreateIntervalDictionary();
            ServerStatus = ServerConnectStatus.Disconnect;
            _mDepthCreator = new GfMarketDepthCreator();
            _orderCreator = new GfOrderCreator(PortfolioNumber);
        }

        private Dictionary<int, string> CreateIntervalDictionary()
        {
            var dictionary = new Dictionary<int, string>();

            dictionary.Add(1, "1m");
            dictionary.Add(5, "5m");
            dictionary.Add(15, "15m");
            dictionary.Add(30, "30m");
            dictionary.Add(60, "1h");
            dictionary.Add(240, "4h");
            dictionary.Add(360, "6h");
            dictionary.Add(720, "12h");
            dictionary.Add(1440, "1d");

            return dictionary;
        }

        /// <summary>
        /// server type
        /// тип сервера
        /// </summary>
        public ServerType ServerType
        {
            get { return ServerType.GateIoFutures; }
        }

        public ServerConnectStatus ServerStatus { get; set; }
        public List<IServerParameter> ServerParameters { get; set; }
        public DateTime ServerTime { get; set; }

        private CancellationTokenSource _cancelTokenSource;

        private readonly ConcurrentQueue<string> _queueMessagesReceivedFromExchange = new ConcurrentQueue<string>();

        public void Connect()
        {
            _publicKey = ((ServerParameterString)ServerParameters[0]).Value;
            _secretKey = ((ServerParameterPassword)ServerParameters[1]).Value;
            _userId = ((ServerParameterString)ServerParameters[2]).Value;
            _baseWallet = ((ServerParameterEnum)ServerParameters[3]).Value;
            _postionMode = ((ServerParameterEnum)ServerParameters[4]).Value;


            _host = "https://fx-api.gateio.ws";
            _baseUrlWss = "wss://fx-ws.gateio.ws/v4/ws";

            if (_baseWallet == "BTC")
                _wallet = "/btc";

            _requestREST = new RestRequestBuilder();

            _cancelTokenSource = new CancellationTokenSource();

            StartMessageReader();

            _signer = new Signer(_secretKey);

            SetDualMode();

            _wsSource = new WsSource(_baseUrlWss + _wallet);
            _wsSource.MessageEvent += WsSourceOnMessageEvent;
            _wsSource.Start();

            _lastTimeUpdateSocket = DateTime.Now;
            ServerStatus = ServerConnectStatus.Connect;
        }

        private void WsSourceOnMessageEvent(WsMessageType msgType, string message)
        {
            switch (msgType)
            {
                case WsMessageType.Opened:
                    ConnectEvent();
                    StartPingAliveLogic();
                    StartPortfolioRequester();
                    break;
                case WsMessageType.Closed:
                    DisconnectEvent();
                    break;
                case WsMessageType.StringData:
                    _queueMessagesReceivedFromExchange.Enqueue(message);
                    break;
                case WsMessageType.Error:
                    SendLogMessage(message, LogMessageType.Error);
                    break;
                default:
                    throw new NotSupportedException(message);
            }
        }

        #region PiniAlive
        private async void PingSender(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(30000, token);

                    if (ServerStatus == ServerConnectStatus.Disconnect)
                    {
                        continue;
                    }
                    SendPing();
                }
                catch (TaskCanceledException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    SendLogMessage("MessageReader error: " + exception, LogMessageType.Error);
                }
            }
        }

        private void StartPingAliveLogic()
        {
            Task.Run(() => SourceAliveCheckerThread(_cancelTokenSource.Token), _cancelTokenSource.Token);
            Task.Run(() => PingSender(_cancelTokenSource.Token), _cancelTokenSource.Token);
        }

        private void SendPing()
        {
            var ping = new FuturesPing() { Time = TimeManager.GetUnixTimeStampSeconds(), Channel = "futures.ping" };
            string message = JsonConvert.SerializeObject(ping);
            _wsSource?.SendMessage(message);
        }

        private void StartMessageReader()
        {
            Task.Run(() => MessageReader(_cancelTokenSource.Token), _cancelTokenSource.Token);
        }

        private async void SourceAliveCheckerThread(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(200);
                if (_lastTimeUpdateSocket == DateTime.MinValue)
                {
                    continue;
                }

                if (_lastTimeUpdateSocket.AddSeconds(120) < DateTime.Now)
                {
                    SendLogMessage("The websocket is disabled. Restart", LogMessageType.Error);
                    Dispose();
                    DisconnectEvent();
                    return;
                }
            }
        }
        #endregion


        private void SetDualMode()
        {
            string mode = _postionMode.Equals("Single") ? "false" : "true";

            string apiKey = _publicKey;
            string apiSecret = _secretKey;
            string host = "https://api.gateio.ws";
            string prefix = "/api/v4";
            string method = "POST";
            string url = "/futures/usdt/dual_mode";
            string queryParam = $"dual_mode={mode}";
            string bodyParam = "";
            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            byte[] bodyBytes = Encoding.UTF8.GetBytes(bodyParam);
            string bodyHash = BitConverter.ToString(SHA512.Create().ComputeHash(bodyBytes)).Replace("-", "").ToLower();

            string signString = $"{method}\n{prefix}{url}\n{queryParam}\n{bodyHash}\n{timestamp}";
            byte[] secretBytes = Encoding.UTF8.GetBytes(apiSecret);
            byte[] signBytes = new HMACSHA512(secretBytes).ComputeHash(Encoding.UTF8.GetBytes(signString));
            string sign = BitConverter.ToString(signBytes).Replace("-", "").ToLower();

            string fullUrl = $"{host}{prefix}{url}?{queryParam}";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Timestamp", timestamp);
                client.DefaultRequestHeaders.Add("KEY", apiKey);
                client.DefaultRequestHeaders.Add("SIGN", sign);

                var response = client.PostAsync(fullUrl, null).Result;
                var responseBody = response.Content.ReadAsStringAsync().Result;
            }
        }

        private async void MessageReader(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!_queueMessagesReceivedFromExchange.IsEmpty)
                    {
                        string mes;

                        if (_queueMessagesReceivedFromExchange.TryDequeue(out mes))
                        {
                            dynamic jt = JToken.Parse(mes);

                            string channel = jt.channel;

                            if (string.IsNullOrEmpty(channel) || channel == "futures.pong" || mes.Contains($@"{{""status"":""success""}}"))
                            {
                                continue;
                            }

                            if (channel == "futures.ping")
                            {
                                var ping = JsonConvert.DeserializeObject<FuturesPing>(mes);
                                var pong = new FuturesPong() { Time = ping.Time, Channel = "futures.pong", Event = "", Error = null, Result = null };
                                string message = JsonConvert.SerializeObject(pong);
                                _wsSource.SendMessage(message);
                            }
                            else if (channel == "futures.order_book")
                            {
                                _lastTimeUpdateSocket = DateTime.Now;

                                var marketDepths = _mDepthCreator.Create(mes);

                                foreach (var depth in marketDepths)
                                {
                                    MarketDepthEvent(depth);
                                }
                            }
                            else if (channel == "futures.orders")
                            {
                                var orders = _orderCreator.Create(mes);

                                foreach (var order in orders)
                                {
                                    MyOrderEvent(order);
                                }
                            }
                            else if (channel == "futures.trades")
                            {
                                foreach (var trade in _orderCreator.TradesCreate(mes))
                                {
                                    NewTradesEvent(trade);
                                }
                            }
                            else if (channel == "futures.usertrades")
                            {
                                var myTrades = _orderCreator.CreateMyTrades(mes);

                                foreach (var myTrade in myTrades)
                                {
                                    MyTradeEvent(myTrade);
                                }
                            }
                            else
                            {
                                SendLogMessage(mes, LogMessageType.System);
                            }
                        }
                    }
                    else
                    {
                        await Task.Delay(20);
                    }
                }
                catch (TaskCanceledException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    SendLogMessage("MessageReader error: " + exception, LogMessageType.Error);
                }
            }
        }

        private void UnInitialize()
        {
            _wsSource.Dispose();
            _wsSource.MessageEvent -= WsSourceOnMessageEvent;
            _wsSource = null;
        }

        public void Dispose()
        {
            try
            {
                if (_wsSource != null)
                {
                    UnInitialize();
                }

                if (_cancelTokenSource != null && !_cancelTokenSource.IsCancellationRequested)
                {
                    _cancelTokenSource.Cancel();
                }
            }
            catch (Exception e)
            {
                SendLogMessage("GateIo dispose error: " + e, LogMessageType.Error);
            }

            ServerStatus = ServerConnectStatus.Disconnect;
        }


        #region исследование плеча 

        public void ChangeLeverage(string securityName, decimal leverage)
        {
            string timeStamp = TimeManager.GetUnixTimeStampSeconds().ToString();
            var headers = new Dictionary<string, string>();
            _requestREST.ClearParams();
            _requestREST.AddParam("leverage", leverage.ToString().Replace(",", "."));

            headers.Add("Timestamp", timeStamp);
            headers.Add("KEY", _publicKey);
            headers.Add("SIGN", _signer.GetSignStringRest("POST", _host + _path + _wallet + "/positions/" + securityName + "/leverage", _requestREST.BuildParams(), "", timeStamp));

            var result = _requestREST.SendPostQuery("POST", _host + _path + _wallet, "/positions/" + securityName + "/leverage", new byte[0], headers);
        }

        public decimal GetLeverage(string securityName)
        {
            string timeStamp = TimeManager.GetUnixTimeStampSeconds().ToString();
            var headers = new Dictionary<string, string>();

            headers.Add("Timestamp", timeStamp);
            headers.Add("KEY", _publicKey);
            headers.Add("SIGN", _signer.GetSignStringRest("GET", _host + _path + _wallet + "/positions/" + securityName, "", "", timeStamp));

            var jsecPosition = _requestREST.SendGetQuery("GET", _host + _path + _wallet + "/positions/" + securityName, "", headers);

            var secPosition = JsonConvert.DeserializeObject<GfPosition>(jsecPosition);

            return Converter.StringToDecimal(secPosition.Leverage);
        }

        public decimal GetMaxLeverage(string securityName)
        {
            string timeStamp = TimeManager.GetUnixTimeStampSeconds().ToString();
            var headers = new Dictionary<string, string>();

            headers.Add("Timestamp", timeStamp);
            headers.Add("KEY", _publicKey);
            headers.Add("SIGN", _signer.GetSignStringRest("GET", _host + _path + _wallet + "/positions/" + securityName, "", "", timeStamp));

            var jsecPosition = _requestREST.SendGetQuery("GET", _host + _path + _wallet + "/positions/" + securityName, "", headers);

            var secPosition = JsonConvert.DeserializeObject<GfPosition>(jsecPosition);

            return Converter.StringToDecimal(secPosition.LeverageMax);
        }
        #endregion 

        #region Запросы
        public void GetSecurities()
        {
            try
            {
                string timeStamp = TimeManager.GetUnixTimeStampSeconds().ToString();
                var headers = new Dictionary<string, string>();
                headers.Add("Timestamp", timeStamp);
                var securitiesJson = _requestREST.SendGetQuery("GET", _host + _path + _wallet, "/contracts", headers);

                var _securities = new List<Security>();

                var jSecurities = JsonConvert.DeserializeObject<GfSecurity[]>(securitiesJson);

                foreach (var jSec in jSecurities)
                {
                    try
                    {
                        Security security = new Security();

                        var name = jSec.Name.ToUpper();

                        security.Name = name;
                        security.NameFull = security.Name;
                        security.NameId = security.Name;
                        security.NameClass = SecurityType.Futures.ToString();
                        security.SecurityType = SecurityType.Futures;
                        security.Decimals = jSec.MarkPriceRound.Split('.')[1].Count();
                        security.Lot = 1;
                        security.PriceStep = Converter.StringToDecimal(jSec.MarkPriceRound);
                        security.PriceStepCost = Converter.StringToDecimal(jSec.MarkPriceRound);
                        security.State = SecurityStateType.Activ;
                        security.Go = jSec.OrderSizeMin;

                        _securities.Add(security);
                    }
                    catch (Exception error)
                    {
                        throw new Exception("Security creation error \n" + error.ToString());
                    }
                }

                SecurityEvent(_securities);
            }
            catch (Exception exception)
            {
                if (exception is WebException)
                {
                    WebException ex = (WebException)exception;
                    HttpWebResponse httpResponse = (HttpWebResponse)ex.Response;
                    if (ex.Response != null)
                    {
                        using (Stream stream = ex.Response.GetResponseStream())
                        {
                            StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                            var log = reader.ReadToEnd();
                            SendLogMessage(log, LogMessageType.Error);
                            return;
                        }
                    }
                }
                SendLogMessage(exception.Message, LogMessageType.Error);
            }
        }

        private void StartPortfolioRequester()
        {
            Task.Run(() => PortfolioRequester(_cancelTokenSource.Token), _cancelTokenSource.Token);
        }

        private async void PortfolioRequester(CancellationToken token)
        {
            if (Portfolios == null)
            {
                Portfolios = new List<Portfolio>();

                var portfolioInitial = new Portfolio();
                portfolioInitial.Number = "GateIoFutures";
                portfolioInitial.ValueBegin = 1;
                portfolioInitial.ValueCurrent = 1;
                portfolioInitial.ValueBlocked = 1;

                Portfolios.Add(portfolioInitial);

                PortfolioEvent(Portfolios);
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(5000, token);

                    GetPortfolios();
                }
                catch (TaskCanceledException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    if (exception is WebException)
                    {
                        WebException ex = (WebException)exception;
                        HttpWebResponse httpResponse = (HttpWebResponse)ex.Response;
                        if (ex.Response != null)
                        {
                            using (Stream stream = ex.Response.GetResponseStream())
                            {
                                StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                                var log = reader.ReadToEnd();
                                SendLogMessage(log, LogMessageType.Error);
                                return;
                            }
                        }
                    }



                    SendLogMessage(exception.Message, LogMessageType.Error);
                }
            }
        }

        public void GetPortfolios()
        {
            try
            {
                string timeStamp = TimeManager.GetUnixTimeStampSeconds().ToString();
                var headers = new Dictionary<string, string>();

                headers.Add("Timestamp", timeStamp);
                headers.Add("KEY", _publicKey);
                headers.Add("SIGN", _signer.GetSignStringRest("GET", _path + _wallet + "/accounts", "", "", timeStamp));

                var result = _requestREST.SendGetQuery("GET", _host + _path + _wallet, "/accounts", headers);

                string jsonPosition = GetPositionSwap(timeStamp);

                if (result.Contains("failed"))
                {
                    SendLogMessage("GateIFutures: Cant get porfolios", LogMessageType.Error);
                    return;
                }

                List<PositionResponceSwap> accountPosition = JsonConvert.DeserializeObject<List<PositionResponceSwap>>(jsonPosition);

                GfAccount accountInfo = JsonConvert.DeserializeObject<GfAccount>(result);

                Portfolio portfolio = Portfolios[0];

                portfolio.ClearPositionOnBoard();

                PositionOnBoard pos = new PositionOnBoard();
                pos.SecurityNameCode = accountInfo.Currency;
                pos.ValueBegin = Converter.StringToDecimal(accountInfo.Total);
                pos.ValueCurrent = Converter.StringToDecimal(accountInfo.Available);
                pos.ValueBlocked = Converter.StringToDecimal(accountInfo.PositionMargin) + Converter.StringToDecimal(accountInfo.OrderMargin);

                portfolio.SetNewPosition(pos);


                foreach (var item in accountPosition)
                {
                    string mode = item.mode.Contains("single") ? "Single" : item.mode;
                    string SellBuy = mode == "Single" ? "_Single" : item.mode.Contains("short") ? "_SHORT" : "_LONG";
                    PositionOnBoard position = new PositionOnBoard();
                    position.PortfolioName = "GateIoFutures";
                    position.SecurityNameCode = item.contract + SellBuy;
                    position.ValueBegin = Math.Abs(Converter.StringToDecimal(item.size));
                    position.ValueCurrent = Math.Abs(Converter.StringToDecimal(item.size));
                    portfolio.SetNewPosition(position);
                }

                PortfolioEvent(Portfolios);
            }
            catch (Exception exception)
            {
                if (exception is WebException)
                {
                    WebException ex = (WebException)exception;
                    HttpWebResponse httpResponse = (HttpWebResponse)ex.Response;
                    if (ex.Response != null)
                    {
                        using (Stream stream = ex.Response.GetResponseStream())
                        {
                            StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                            var log = reader.ReadToEnd();
                            SendLogMessage(log, LogMessageType.Error);
                            return;
                        }
                    }
                }
                SendLogMessage(exception.Message, LogMessageType.Error);
            }
        }

        private string GetPositionSwap(string timeStamp)
        {
            HttpClient httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Timestamp", timeStamp);
            httpClient.DefaultRequestHeaders.Add("KEY", _publicKey);
            httpClient.DefaultRequestHeaders.Add("SIGN", _signer.GetSignStringRest("GET", "/api/v4" + "/futures/usdt/positions", "", "", timeStamp));
            var responce = httpClient.GetAsync("https://api.gateio.ws/api/v4/futures/usdt/positions").Result;
            var json = responce.Content.ReadAsStringAsync().Result;
            return json;
        }

        public void SendOrder(Order order)
        {
            decimal outputVolume = order.Volume;
            if (order.Side == Side.Sell)
                outputVolume = -1 * order.Volume;

            string bodyContent = String.Empty;
            if (_postionMode.Equals("Double") &&
                order.PositionConditionType == OrderPositionConditionType.Close)
            {
                string close = null;
                close = order.Side == Side.Sell ? "close_long" : "close_short";

                CreateOrderRequstDoubleModeClose jOrder = new CreateOrderRequstDoubleModeClose()
                {
                    Contract = order.SecurityNameCode,
                    Iceberg = 0,
                    Price = order.Price.ToString(CultureInfo.InvariantCulture),
                    Size = Convert.ToInt64(outputVolume),
                    Tif = "gtc",
                    Text = $"t-{order.NumberUser}",
                    Close = false,
                    Reduce_only = true
                };

                bodyContent = JsonConvert.SerializeObject(jOrder).Replace(" ", "").Replace(Environment.NewLine, "");
            }
            else
            {
                CreateOrderRequst jOrder = new CreateOrderRequst()
                {
                    Contract = order.SecurityNameCode,
                    Iceberg = 0,
                    Price = order.Price.ToString(CultureInfo.InvariantCulture),
                    Size = Convert.ToInt64(outputVolume),
                    Tif = "gtc",
                    Text = $"t-{order.NumberUser}",
                };

                bodyContent = JsonConvert.SerializeObject(jOrder).Replace(" ", "").Replace(Environment.NewLine, "");
            }





            string timeStamp = TimeManager.GetUnixTimeStampSeconds().ToString();
            var headers = new Dictionary<string, string>();

            headers.Add("Timestamp", timeStamp);
            headers.Add("KEY", _publicKey);
            headers.Add("SIGN", _signer.GetSignStringRest("POST", _path + _wallet + "/orders", "", bodyContent, timeStamp));
            headers.Add("X-Gate-Channel-Id", "osa");

            try
            {
                var result = _requestREST.SendPostQuery("POST", _host + _path + _wallet, "/orders", Encoding.UTF8.GetBytes(bodyContent), headers);



                CreateOrderResponse orderResponse = JsonConvert.DeserializeObject<CreateOrderResponse>(result);

                if (orderResponse.Status == "finished")
                {
                    SendLogMessage($"Order num {order.NumberUser} on exchange.", LogMessageType.Trade);
                }
                else if (orderResponse.Status == "open")
                {
                    SendLogMessage($"Order num {order.NumberUser} wait to execution on exchange.", LogMessageType.Trade);
                }
                else
                {
                    //err_msg
                    dynamic errorData = JToken.Parse(result);
                    string errorMsg = errorData.err_msg;

                    SendLogMessage($"Order exchange error num {order.NumberUser} : {errorMsg}", LogMessageType.Error);

                    order.State = OrderStateType.Fail;

                    MyOrderEvent(order);
                }
            }
            catch (Exception exception)
            {
                if (exception is WebException)
                {
                    WebException ex = (WebException)exception;
                    HttpWebResponse httpResponse = (HttpWebResponse)ex.Response;
                    if (ex.Response != null)
                    {
                        using (Stream stream = ex.Response.GetResponseStream())
                        {
                            StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                            var log = reader.ReadToEnd();
                            SendLogMessage(log, LogMessageType.Error);
                            return;
                        }
                    }
                }
                SendLogMessage(exception.Message, LogMessageType.Error);
            }
        }

        public void CancelOrder(Order order)
        {
            try
            {
                string timeStamp = TimeManager.GetUnixTimeStampSeconds().ToString();
                var headers = new Dictionary<string, string>();

                headers.Add("Timestamp", timeStamp);
                headers.Add("KEY", _publicKey);
                headers.Add("SIGN", _signer.GetSignStringRest("DELETE", _path + _wallet + $"/orders/{order.NumberMarket}", "", "", timeStamp));

                var result = _requestREST.SendGetQuery("DELETE", _host + _path + _wallet, $"/orders/{order.NumberMarket}", headers);

                CancelOrderResponse cancelResponse = JsonConvert.DeserializeObject<CancelOrderResponse>(result);

                if (cancelResponse.FinishAs == "cancelled")
                {
                    SendLogMessage($"Order num {order.NumberUser} canceled.", LogMessageType.Trade);
                    order.State = OrderStateType.Cancel;
                    MyOrderEvent(order);
                }
                else
                {
                    SendLogMessage($"Error on order cancel num {order.NumberUser}", LogMessageType.Error);
                }
            }
            catch (Exception exception)
            {
                if (exception is WebException)
                {
                    WebException ex = (WebException)exception;
                    HttpWebResponse httpResponse = (HttpWebResponse)ex.Response;
                    if (ex.Response != null)
                    {
                        using (Stream stream = ex.Response.GetResponseStream())
                        {
                            StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                            var log = reader.ReadToEnd();
                            SendLogMessage(log, LogMessageType.Error);
                            return;
                        }
                    }
                }
                SendLogMessage(exception.Message, LogMessageType.Error);
            }
        }

        #endregion

        #region Подписка на данные
        public void Subscrible(Security security)
        {
            SubscribeMarketDepth(security.Name);
            SubscribeTrades(security.Name);
            SubscribeOrders(security.Name);
            SubscribeMyTrades(security.Name);
        }

        private void SubscribeMarketDepth(string security)
        {
            List<string> payload = new List<string>();
            payload.Add(security);
            payload.Add("20");
            payload.Add("0");

            WsRequestBuilder request = new WsRequestBuilder(TimeManager.GetUnixTimeStampSeconds(), "futures.order_book", "subscribe", payload.ToArray());

            string message = request.GetPublicRequest();

            _wsSource?.SendMessage(message);
        }

        private void SubscribeTrades(string security)
        {
            List<string> payload = new List<string>();
            payload.Add(security);

            WsRequestBuilder request = new WsRequestBuilder(TimeManager.GetUnixTimeStampSeconds(), "futures.trades", "subscribe", payload.ToArray());

            string message = request.GetPublicRequest();

            _wsSource?.SendMessage(message);
        }

        private void SubscribeOrders(string security)
        {
            List<string> payload = new List<string>();
            payload.Add(_userId);
            payload.Add(security);

            WsRequestBuilder request = new WsRequestBuilder(TimeManager.GetUnixTimeStampSeconds(), "futures.orders", "subscribe", payload.ToArray());

            string message = request.GetPrivateRequest(_publicKey, _secretKey);

            _wsSource?.SendMessage(message);
        }

        private void SubscribeMyTrades(string security)
        {
            List<string> payload = new List<string>();
            payload.Add(_userId);
            payload.Add(security);

            WsRequestBuilder request = new WsRequestBuilder(TimeManager.GetUnixTimeStampSeconds(), "futures.usertrades", "subscribe", payload.ToArray());

            string message = request.GetPrivateRequest(_publicKey, _secretKey);

            _wsSource?.SendMessage(message);
        }

        public void GetOrdersState(List<Order> orders)
        {

        }
        #endregion

        #region Работа со свечами

        public List<Candle> GetCandleDataToSecurity(Security security, TimeFrameBuilder timeFrameBuilder, DateTime startTime, DateTime endTime, DateTime actualTime)
        {
            List<Candle> candles = new List<Candle>();

            int oldInterval = Convert.ToInt32(timeFrameBuilder.TimeFrameTimeSpan.TotalMinutes);

            var step = new TimeSpan(0, (int)(oldInterval * 1000), 0);

            actualTime = startTime;

            var midTime = actualTime + step;

            while (true)
            {
                if (actualTime >= endTime)
                {
                    break;
                }

                if (midTime > endTime)
                    midTime = endTime;

                List<Candle> newCandles = GetCandles(oldInterval, security.Name, actualTime, midTime);

                if (newCandles != null && newCandles.Count != 0)
                    candles.AddRange(newCandles);

                actualTime = candles[candles.Count - 1].TimeStart.AddMinutes(oldInterval);
                midTime = actualTime + step;
                Thread.Sleep(100);
            }

            if (candles.Count == 0)
            {
                return null;
            }

            return candles;
        }



        public List<Candle> GetCandleHistory(string nameSec, TimeSpan tf)
        {
            var diff = new TimeSpan(0, (int)(tf.TotalMinutes * 1000), 0);

            return GetCandles((int)tf.TotalMinutes, nameSec, DateTime.UtcNow - diff, DateTime.UtcNow);
        }

        private object _locker = new object();

        private List<Candle> GetCandles(int oldInterval, string security, DateTime startTime, DateTime endTime)
        {
            lock (_locker)
            {
                try
                {
                    var needIntervalForQuery = CandlesCreator.DetermineAppropriateIntervalForRequest(oldInterval, _supportedIntervals, out var needInterval);

                    var from = TimeManager.GetTimeStampSecondsToDateTime(startTime);
                    var to = TimeManager.GetTimeStampSecondsToDateTime(endTime);

                    string timeStamp = TimeManager.GetUnixTimeStampSeconds().ToString();
                    var headers = new Dictionary<string, string>();
                    headers.Add("Timestamp", timeStamp);

                    RestRequestBuilder requestBuilder = new RestRequestBuilder();
                    requestBuilder.AddParam("contract", security);
                    requestBuilder.AddParam("from", from.ToString());
                    requestBuilder.AddParam("to", to.ToString());
                    requestBuilder.AddParam("interval", needIntervalForQuery);

                    PublicUrlBuilder urlBuilder = new PublicUrlBuilder(_host, _path, _wallet);

                    var candlesJson = _requestREST.SendGetQuery("GET", "", urlBuilder.Build("/candlesticks", requestBuilder), headers);

                    var candlesOut = JsonConvert.DeserializeObject<GfCandle[]>(candlesJson);

                    var oldCandles = CreateCandlesFromJson(candlesOut);

                    if (oldInterval == needInterval)
                    {
                        return oldCandles;
                    }

                    var newCandles = CandlesCreator.CreateCandlesRequiredInterval(needInterval, oldInterval, oldCandles);

                    return newCandles;
                }
                catch
                {
                    SendLogMessage(OsLocalization.Market.Message95 + security, LogMessageType.Error);

                    return null;
                }
            }
        }

        private List<Candle> CreateCandlesFromJson(GfCandle[] gfCandles)
        {
            List<Candle> candles = new List<Candle>();

            foreach (var jtCandle in gfCandles)
            {
                var candle = new Candle();

                candle.TimeStart = TimeManager.GetDateTimeFromTimeStampSeconds(jtCandle.T);
                candle.Open = Converter.StringToDecimal(jtCandle.O);
                candle.High = Converter.StringToDecimal(jtCandle.H);
                candle.Low = Converter.StringToDecimal(jtCandle.L);
                candle.Close = Converter.StringToDecimal(jtCandle.C);
                candle.Volume = jtCandle.V;

                candles.Add(candle);
            }

            return candles;
        }

        public List<Trade> GetTickDataToSecurity(Security security, DateTime startTime, DateTime endTime, DateTime actualTime)
        {
            List<Trade> trades = new List<Trade>();

            DateTime endOver = endTime;

            while (true)
            {
                if (endOver <= startTime)
                {
                    break;
                }

                List<Trade> newTrades = GetTrades(security.Name, endOver);

                if (newTrades != null && newTrades.Count != 0)
                    trades.AddRange(newTrades);
                else
                    break;

                endOver = trades[trades.Count - 1].Time.AddSeconds(-1);
                Thread.Sleep(100);
            }

            if (trades.Count == 0)
            {
                return null;
            }


            while (trades.Last().Time < startTime)
            {
                trades.Remove(trades.Last());
            }

            trades.Reverse();

            return trades;
        }

        private List<Trade> GetTrades(string security, DateTime endTime)
        {
            lock (_locker)
            {
                try
                {
                    var to = TimeManager.GetTimeStampSecondsToDateTime(endTime);

                    string timeStamp = TimeManager.GetUnixTimeStampSeconds().ToString();
                    var headers = new Dictionary<string, string>();
                    headers.Add("Timestamp", timeStamp);

                    RestRequestBuilder requestBuilder = new RestRequestBuilder();
                    requestBuilder.AddParam("contract", security);
                    requestBuilder.AddParam("limit", "1000");
                    requestBuilder.AddParam("to", to.ToString());

                    PublicUrlBuilder urlBuilder = new PublicUrlBuilder(_host, _path, _wallet);

                    var tradesJson = _requestREST.SendGetQuery("GET", "", urlBuilder.Build("/trades", requestBuilder), headers);

                    var tradesOut = JsonConvert.DeserializeObject<GfTrade[]>(tradesJson);

                    var oldTrades = CreateTradesFromJson(tradesOut);

                    return oldTrades;
                }
                catch
                {
                    SendLogMessage(OsLocalization.Market.Message95 + security, LogMessageType.Error);

                    return null;
                }
            }
        }

        private List<Trade> CreateTradesFromJson(GfTrade[] gfTrades)
        {
            List<Trade> trades = new List<Trade>();

            foreach (var jtTrade in gfTrades)
            {
                var trade = new Trade();

                trade.Time = TimeManager.GetDateTimeFromTimeStampSeconds(jtTrade.CreateTime);
                trade.Price = Converter.StringToDecimal(jtTrade.Price);
                trade.MicroSeconds = 0;
                trade.Id = jtTrade.Id.ToString();
                trade.Volume = Math.Abs(jtTrade.Size);
                trade.SecurityNameCode = jtTrade.Contract;

                if (jtTrade.Size >= 0)
                {
                    trade.Side = Side.Buy;
                    trade.Ask = 0;
                    trade.AsksVolume = 0;
                    trade.Bid = trade.Price;
                    trade.BidsVolume = trade.Volume;
                }
                else if (jtTrade.Size < 0)
                {
                    trade.Side = Side.Sell;
                    trade.Ask = trade.Price;
                    trade.AsksVolume = trade.Volume;
                    trade.Bid = 0;
                    trade.BidsVolume = 0;
                }


                trades.Add(trade);
            }

            return trades;
        }

        public void CancelAllOrders()
        {

        }

        public void ResearchTradesToOrders(List<Order> orders)
        {

        }

        private void SendLogMessage(string messgae, LogMessageType logMessageType)
        {
            LogMessageEvent(messgae, logMessageType);
        }

        public event Action<Order> MyOrderEvent;
        public event Action<MyTrade> MyTradeEvent;
        public event Action<List<Portfolio>> PortfolioEvent;
        public event Action<List<Security>> SecurityEvent;
        public event Action<MarketDepth> MarketDepthEvent;
        public event Action<Trade> NewTradesEvent;
        public event Action ConnectEvent;
        public event Action DisconnectEvent;
        public event Action<string, LogMessageType> LogMessageEvent;

        #endregion

        public void CancelAllOrdersToSecurity(Security security)
        {

        }
    }
}
