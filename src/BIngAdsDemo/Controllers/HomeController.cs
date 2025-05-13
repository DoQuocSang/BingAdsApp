using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.BingAds;
using Microsoft.BingAds.V13.CustomerManagement;
using System.ServiceModel;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Extensions;
using Newtonsoft.Json;
using System.Text;
using BingAdsDemo.BingAds;

namespace BingAdsDemo.Controllers
{
    public class HomeController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private static AuthorizationData _authorizationData;
        private static ServiceClient<ICustomerManagementService> _customerManagementService;
        private static string ClientState = "ClientStateGoesHere";
        private static string _output = "";

        public HomeController(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
        {
            _configuration = configuration;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var session = _httpContextAccessor.HttpContext.Session;

                OAuthWebAuthCodeGrant oAuthWebAuthCodeGrant;

                var apiEnvironment = _configuration["BingAds:Environment"] == "Sandbox"
                  ? ApiEnvironment.Sandbox
                  : ApiEnvironment.Production;

                var authTokensString = session.GetString("authTokens");

                if (!String.IsNullOrWhiteSpace(authTokensString))
                {
                    var authTokens = JsonConvert.DeserializeObject<OAuthTokens>(authTokensString);
                    oAuthWebAuthCodeGrant = new OAuthWebAuthCodeGrant(
                        _configuration["BingAds:ClientId"],
                        _configuration["BingAds:ClientSecret"],
                        new Uri(_configuration["BingAds:RedirectionUri"]),
                        authTokens,
                        apiEnvironment,
                         OAuthScope.MSADS_MANAGE,
                        _configuration["BingAds:TenantId"]
                    )
                    {
                        State = ClientState
                    };
                    return await SetAuthorizationDataAsync(oAuthWebAuthCodeGrant);
                }

                oAuthWebAuthCodeGrant = new OAuthWebAuthCodeGrant(
                    _configuration["BingAds:ClientId"],
                    _configuration["BingAds:ClientSecret"],
                    new Uri(_configuration["BingAds:RedirectionUri"]),
                    apiEnvironment,
                     OAuthScope.MSADS_MANAGE,
                    _configuration["BingAds:TenantId"]
                )
                {
                    State = ClientState
                };

                oAuthWebAuthCodeGrant.NewOAuthTokensReceived += (sender, args) =>
                {
                    SaveRefreshToken(args.NewRefreshToken);
                };

                if (RefreshTokenExists())
                {
                    await oAuthWebAuthCodeGrant.RequestAccessAndRefreshTokensAsync(GetRefreshToken());
                    SaveAuthTokensToSession(oAuthWebAuthCodeGrant);
                    return await SetAuthorizationDataAsync(oAuthWebAuthCodeGrant);
                }

                if (Request.Query.ContainsKey("code"))
                {
                    if (Request.Query["state"] != ClientState)
                        throw new HttpRequestException("The OAuth response state does not match the client request state.");

                    await oAuthWebAuthCodeGrant.RequestAccessAndRefreshTokensAsync(new Uri(Request.GetEncodedUrl()));
                    SaveAuthTokensToSession(oAuthWebAuthCodeGrant);
                    return await SetAuthorizationDataAsync(oAuthWebAuthCodeGrant);
                }

                SaveRefreshToken(oAuthWebAuthCodeGrant.OAuthTokens?.RefreshToken);

                return Redirect(oAuthWebAuthCodeGrant.GetAuthorizationEndpoint().ToString());
            }
            catch (OAuthTokenRequestException ex)
            {
                ViewBag.Errors = $"Couldn't get OAuth tokens. Error: {ex.Details.Error}. Description: {ex.Details.Description}";
                return View();
            }
            catch (FaultException<Microsoft.BingAds.V13.CustomerManagement.AdApiFaultDetail> ex)
            {
                ViewBag.Errors = string.Join("; ", ex.Detail.Errors.Select(error => $"{error.Code}: {error.Message}"));
                return View();
            }
            catch (FaultException<Microsoft.BingAds.V13.CustomerManagement.ApiFault> ex)
            {
                ViewBag.Errors = string.Join("; ", ex.Detail.OperationErrors.Select(error => $"{error.Code}: {error.Message}"));
                return View();
            }
            catch (Exception ex)
            {
                ViewBag.Errors = ex.Message;
                return View();
            }
        }

        private async Task<IActionResult> SetAuthorizationDataAsync(Authentication authentication)
        {
            _authorizationData = new AuthorizationData
            {
                Authentication = authentication,
                DeveloperToken = _configuration["BingAds:DeveloperToken"]
            };

            _customerManagementService = new ServiceClient<ICustomerManagementService>(_authorizationData);

            var getUserRequest = new GetUserRequest { UserId = null };
            var getUserResponse = await _customerManagementService.CallAsync((s, r) => s.GetUserAsync(r), getUserRequest);
            var user = getUserResponse.User;

            var predicate = new Predicate
            {
                Field = "UserId",
                Operator = PredicateOperator.Equals,
                Value = user.Id.ToString()
            };

            var searchAccountsRequest = new SearchAccountsRequest
            {
                PageInfo = new Paging { Index = 0, Size = 10 },
                Predicates = new[] { predicate }
            };

            var searchAccountsResponse = await _customerManagementService.CallAsync((s, r) => s.SearchAccountsAsync(r), searchAccountsRequest);
            var accounts = searchAccountsResponse.Accounts.ToArray();
            if (accounts.Length == 0) return View();

            _authorizationData.AccountId = (long)accounts[0].Id;
            _authorizationData.CustomerId = (int)accounts[0].ParentCustomerId;

            OutputArrayOfAdvertiserAccount(accounts);
            ViewBag.Accounts = _output;
            _output = null;

            // Ad Extension example
            var adExtension = new AdExtensions();
            adExtension.RunAsync(_authorizationData);

            // Reponsive Search Ads example
            var reponsiveSearchAd = new ResponsiveSearchAds();
            reponsiveSearchAd.RunAsync(_authorizationData);

            // Responsive Ads example
            var reponsiveAd = new ResponsiveAds();
            reponsiveAd.RunAsync(_authorizationData);

            return View();
        }

        private void SaveAuthTokensToSession(OAuthWebAuthCodeGrant auth)
        {
            var session = _httpContextAccessor.HttpContext.Session;
            var authString = JsonConvert.SerializeObject(auth.OAuthTokens);
            session.SetString("authTokens", authString);
        }

        private void SaveRefreshToken(string refreshToken)
        {
            _httpContextAccessor.HttpContext.Session.SetString("RefreshToken", refreshToken ?? string.Empty);
        }

        private bool RefreshTokenExists()
        {
            return !string.IsNullOrEmpty(_httpContextAccessor.HttpContext.Session.GetString("RefreshToken"));
        }

        private string GetRefreshToken()
        {
            return _httpContextAccessor.HttpContext.Session.GetString("RefreshToken");
        }

        #region OutputHelpers
        private static void OutputArrayOfAdvertiserAccount(IList<AdvertiserAccount> dataObjects)
        {
            if (null != dataObjects)
            {
                foreach (var dataObject in dataObjects)
                {
                    OutputAdvertiserAccount(dataObject);
                    OutputStatusMessage("\n");
                }
            }
        }

        private static void OutputAdvertiserAccount(AdvertiserAccount dataObject)
        {
            if (null != dataObject)
            {
                OutputStatusMessage(string.Format("BillToCustomerId: {0}", dataObject.BillToCustomerId));
                OutputStatusMessage(string.Format("CurrencyCode: {0}", dataObject.CurrencyCode));
                OutputStatusMessage(string.Format("AccountFinancialStatus: {0}", dataObject.AccountFinancialStatus));
                OutputStatusMessage(string.Format("Id: {0}", dataObject.Id));
                OutputStatusMessage(string.Format("Language: {0}", dataObject.Language));
                OutputStatusMessage(string.Format("LastModifiedByUserId: {0}", dataObject.LastModifiedByUserId));
                OutputStatusMessage(string.Format("LastModifiedTime: {0}", dataObject.LastModifiedTime));
                OutputStatusMessage(string.Format("Name: {0}", dataObject.Name));
                OutputStatusMessage(string.Format("Number: {0}", dataObject.Number));
                OutputStatusMessage(string.Format("ParentCustomerId: {0}", dataObject.ParentCustomerId));
                OutputStatusMessage(string.Format("PaymentMethodId: {0}", dataObject.PaymentMethodId));
                OutputStatusMessage(string.Format("PaymentMethodType: {0}", dataObject.PaymentMethodType));
                OutputStatusMessage(string.Format("PrimaryUserId: {0}", dataObject.PrimaryUserId));
                OutputStatusMessage(string.Format("AccountLifeCycleStatus: {0}", dataObject.AccountLifeCycleStatus));
                OutputStatusMessage(string.Format("TimeStamp: {0}", dataObject.TimeStamp));
                OutputStatusMessage(string.Format("TimeZone: {0}", dataObject.TimeZone));
                OutputStatusMessage(string.Format("PauseReason: {0}", dataObject.PauseReason));
                OutputArrayOfKeyValuePairOfstringstring(dataObject.ForwardCompatibilityMap);
                OutputArrayOfCustomerInfo(dataObject.LinkedAgencies);
                OutputStatusMessage(string.Format("SalesHouseCustomerId: {0}", dataObject.SalesHouseCustomerId));
                OutputArrayOfKeyValuePairOfstringstring(dataObject.TaxInformation);
                OutputStatusMessage(string.Format("BackUpPaymentInstrumentId: {0}", dataObject.BackUpPaymentInstrumentId));
                OutputStatusMessage(string.Format("BillingThresholdAmount: {0}", dataObject.BillingThresholdAmount));
                OutputAddress(dataObject.BusinessAddress);
                OutputStatusMessage(string.Format("AutoTagType: {0}", dataObject.AutoTagType));
                OutputStatusMessage(string.Format("SoldToPaymentInstrumentId: {0}", dataObject.SoldToPaymentInstrumentId));
            }
        }

        private static void OutputAddress(Address dataObject)
        {
            if (null != dataObject)
            {
                OutputStatusMessage(string.Format("City: {0}", dataObject.City));
                OutputStatusMessage(string.Format("CountryCode: {0}", dataObject.CountryCode));
                OutputStatusMessage(string.Format("Id: {0}", dataObject.Id));
                OutputStatusMessage(string.Format("Line1: {0}", dataObject.Line1));
                OutputStatusMessage(string.Format("Line2: {0}", dataObject.Line2));
                OutputStatusMessage(string.Format("Line3: {0}", dataObject.Line3));
                OutputStatusMessage(string.Format("Line4: {0}", dataObject.Line4));
                OutputStatusMessage(string.Format("PostalCode: {0}", dataObject.PostalCode));
                OutputStatusMessage(string.Format("StateOrProvince: {0}", dataObject.StateOrProvince));
                OutputStatusMessage(string.Format("TimeStamp: {0}", dataObject.TimeStamp));
                OutputStatusMessage(string.Format("BusinessName: {0}", dataObject.BusinessName));
            }
        }

        private static void OutputArrayOfKeyValuePairOfstringstring(IList<KeyValuePair<string, string>> dataObjects)
        {
            if (null != dataObjects)
            {
                foreach (var dataObject in dataObjects)
                {
                    OutputKeyValuePairOfstringstring(dataObject);
                }
            }
        }

        private static void OutputKeyValuePairOfstringstring(KeyValuePair<string, string> dataObject)
        {
            if (null != dataObject.Key)
            {
                OutputStatusMessage(string.Format("key: {0}", dataObject.Key));
                OutputStatusMessage(string.Format("value: {0}", dataObject.Value));
            }
        }

        private static void OutputCustomerInfo(CustomerInfo dataObject)
        {
            if (null != dataObject)
            {
                OutputStatusMessage(string.Format("Id: {0}", dataObject.Id));
                OutputStatusMessage(string.Format("Name: {0}", dataObject.Name));
            }
        }

        private static void OutputArrayOfCustomerInfo(IList<CustomerInfo> dataObjects)
        {
            if (null != dataObjects)
            {
                foreach (var dataObject in dataObjects)
                {
                    OutputCustomerInfo(dataObject);
                    OutputStatusMessage("\n");
                }
            }
        }

        private static void OutputStatusMessage(String msg)
        {
            _output += (msg + "<br/>");
        }
        #endregion
    }
}
