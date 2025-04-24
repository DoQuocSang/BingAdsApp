using System.Diagnostics;
using BingAdsWebApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.BingAds;
using Microsoft.BingAds.V13.CustomerManagement;
using System.Threading.Tasks;
using BingAdsWebApp.BingAds;
using System.ServiceModel;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Extensions;
using BingAdsWebApp.Services;

namespace BingAdsWebApp.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration;
        private readonly TokenService _tokenService;

        private readonly BingAdsOptions _bingAdsOptions;
        private static AuthorizationData _authorizationData;
        private static ServiceClient<ICustomerManagementService> _customerManagementService;
        private static string ClientState = "ClientStateGoesHere";
        private static string _output = "";

        public HomeController(
            ILogger<HomeController> logger, 
            IConfiguration configuration,
            TokenService tokenService
        )
        {
            _logger = logger;
            _configuration = configuration;
            _tokenService = tokenService;
        }

        /// <summary>
        /// Controls the contents displayed at Index.cshtml.
        /// </summary>
        public async Task<IActionResult> Index()
        {
            //HttpContext.Session.Clear();
            try
            {
                // If there is already an authenticated Microsoft account during this HTTP session, 
                // go ahead and call Bing Ads API service operations.
                var sessionAuth = HttpContext.Session.GetString("auth");
                if (!string.IsNullOrEmpty(sessionAuth))
                {
                    return await SetAuthorizationDataAsync(JsonSerializer.Deserialize<OAuthWebAuthCodeGrant>(sessionAuth));
                }

                // Prepare the OAuth object for use with the authorization code grant flow. 

                var apiEnvironment =
                    _configuration["BingAds:Environment"] == ApiEnvironment.Sandbox.ToString() ?
                    ApiEnvironment.Sandbox : ApiEnvironment.Production;

                var tenantId = _configuration["BingAds:TenantId"];

                var oAuthWebAuthCodeGrant = new OAuthWebAuthCodeGrant(
                    _configuration["BingAds:ClientId"],
                    _configuration["BingAds:ClientSecret"],
                    new Uri(_configuration["BingAds:RedirectionUri"]),
                    apiEnvironment,
                    OAuthScope.MSADS_MANAGE,
                    _configuration["BingAds:TenantId"]);

                // It is recommended that you specify a non guessable 'state' request parameter to help prevent
                // cross site request forgery (CSRF). 
                oAuthWebAuthCodeGrant.State = ClientState;

                // When calling Bing Ads API service operations with ServiceClient or BulkServiceManager, each will refresh your access token 
                // automatically if they detect the AuthenticationTokenExpired (109) error code. 
                // As a best practice you should always use the most recent provided refresh token.
                // Save the refresh token whenever new OAuth tokens are received by subscribing to the NewOAuthTokensReceived event handler. 

                oAuthWebAuthCodeGrant.NewOAuthTokensReceived +=
                    (sender, args) => _tokenService.SaveRefreshToken(args.NewRefreshToken);

                // If a refresh token is already present, use it to request new access and refresh tokens.

                if (_tokenService.RefreshTokenExists())
                {
                    await oAuthWebAuthCodeGrant.RequestAccessAndRefreshTokensAsync(_tokenService.GetRefreshToken());

                    // Save the authentication object in a session for future requests.
                    HttpContext.Session.SetString("auth", JsonSerializer.Serialize(oAuthWebAuthCodeGrant));

                    return await SetAuthorizationDataAsync(oAuthWebAuthCodeGrant);
                }

                // If the current HTTP request is a callback from the Microsoft Account authorization server,
                // use the current request url containing authorization code to request new access and refresh tokens
                if (!string.IsNullOrEmpty(HttpContext.Request.Query["code"]))
                {
                    if (oAuthWebAuthCodeGrant.State != ClientState)
                        throw new HttpRequestException("The OAuth response state does not match the client request state.");

                    //await oAuthWebAuthCodeGrant.RequestAccessAndRefreshTokensAsync(HttpContext.Request.Query["code"]);
                    var responseUri= new Uri(HttpContext.Request.GetDisplayUrl());
                    await oAuthWebAuthCodeGrant.RequestAccessAndRefreshTokensAsync(responseUri);

                    // Save the authentication object in a session for future requests. 
                    HttpContext.Session.SetString("auth", JsonSerializer.Serialize(oAuthWebAuthCodeGrant));

                    return await SetAuthorizationDataAsync(oAuthWebAuthCodeGrant);
                }

                _tokenService.SaveRefreshToken(oAuthWebAuthCodeGrant.OAuthTokens?.RefreshToken);

                // If there is no refresh token saved and no callback from the authorization server, 
                // then connect to the authorization server and request user consent. 
                return Redirect(oAuthWebAuthCodeGrant.GetAuthorizationEndpoint().ToString());
            }
            // Catch authentication exceptions
            //catch (OAuthTokenRequestException ex)
            //{
            //    ViewBag.Errors = (string.Format("Couldn't get OAuth tokens. Error: {0}. Description: {1}",
            //        ex.Details.Error, ex.Details.Description));
            //    return View();
            //}

            catch (OAuthTokenRequestException ex)
            {
                var errorMessage = $"Couldn't get OAuth tokens.\nError: {ex.Details.Error}\nDescription: {ex.Details.Description}";
                ViewBag.Errors = errorMessage;

                Console.WriteLine(errorMessage);
                return View();
            } 
            // Catch Customer Management service exceptions
            catch (FaultException<Microsoft.BingAds.V13.CustomerManagement.AdApiFaultDetail> ex)
            {
                ViewBag.Errors = (string.Join("; ", ex.Detail.Errors.Select(
                    error => string.Format("{0}: {1}", error.Code, error.Message))));
                return View();
            }
            catch (FaultException<Microsoft.BingAds.V13.CustomerManagement.ApiFault> ex)
            {
                ViewBag.Errors = (string.Join("; ", ex.Detail.OperationErrors.Select(
                    error => string.Format("{0}: {1}", error.Code, error.Message))));
                return View();
            }
            catch (Exception ex)
            {
                ViewBag.Errors = ex.Message;
                return View();
            }
        }

        /// <summary>
        /// Adds a campaign to an account of the current authenticated user. 
        /// </summary>
        private async Task<IActionResult> SetAuthorizationDataAsync(Authentication authentication)
        {
            _authorizationData = new AuthorizationData
            {
                Authentication = authentication,
                DeveloperToken = _configuration["BingAds:DeveloperToken"]
            };

            _customerManagementService = new ServiceClient<ICustomerManagementService>(_authorizationData);

            var getUserRequest = new GetUserRequest
            {
                UserId = null
            };

            var getUserResponse =
                (await _customerManagementService.CallAsync((s, r) => s.GetUserAsync(r), getUserRequest));
            var user = getUserResponse.User;

            var predicate = new Predicate
            {
                Field = "UserId",
                Operator = PredicateOperator.Equals,
                Value = user.Id.ToString()
            };

            var paging = new Paging
            {
                Index = 0,
                Size = 10
            };

            var searchAccountsRequest = new SearchAccountsRequest
            {
                Ordering = null,
                PageInfo = paging,
                Predicates = new[] { predicate }
            };

            var searchAccountsResponse =
                (await _customerManagementService.CallAsync((s, r) => s.SearchAccountsAsync(r), searchAccountsRequest));

            var accounts = searchAccountsResponse.Accounts.ToArray();
            if (accounts.Length <= 0) return View();

            _authorizationData.AccountId = (long)accounts[0].Id;
            _authorizationData.CustomerId = (int)accounts[0].ParentCustomerId;

            OutputArrayOfAdvertiserAccount(accounts);

            ViewBag.Accounts = _output;
            _output = null;

            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }


        #region OutputHelpers

        /**
         * You can extend the app with example output helpers at:
         * https://github.com/BingAds/BingAds-dotNet-SDK/tree/main/examples/BingAdsExamples/BingAdsExamplesLibrary/v13
         * 
         * AdInsightExampleHelper.cs
         * BulkExampleHelper.cs
         * CampaignManagementExampleHelper.cs
         * CustomerBillingExampleHelper.cs
         * CustomerManagementExampleHelper.cs
         * ReportingExampleHelper.cs
         **/

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

        #endregion OutputHelpers
    }
}
