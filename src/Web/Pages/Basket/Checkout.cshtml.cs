using Ardalis.GuardClauses;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Web.Interfaces;
using Newtonsoft.Json;

namespace Microsoft.eShopWeb.Web.Pages.Basket;

[Authorize]
public class CheckoutModel : PageModel
{
    private readonly IBasketService _basketService;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IOrderService _orderService;
    private string? _username = null;
    private readonly IBasketViewModelService _basketViewModelService;
    private readonly IAppLogger<CheckoutModel> _logger;

    private const string OrderDetailsFunctionUrl = "https://aibulatprocessor.azurewebsites.net/api/DeliveryOrderProcessor?code=9Kjj5Fe1nCrFhxGT5WhohlyLmQJGuoSqUsM4fsfkovKbAzFu1DaovA==";

    public CheckoutModel(IBasketService basketService,
        IBasketViewModelService basketViewModelService,
        SignInManager<ApplicationUser> signInManager,
        IOrderService orderService,
        IAppLogger<CheckoutModel> logger)
    {
        _basketService = basketService;
        _signInManager = signInManager;
        _orderService = orderService;
        _basketViewModelService = basketViewModelService;
        _logger = logger;
    }

    public BasketViewModel BasketModel { get; set; } = new BasketViewModel();

    public async Task OnGet()
    {
        await SetBasketModelAsync();
    }

    public async Task<IActionResult> OnPost(IEnumerable<BasketItemViewModel> items)
    {
        try
        {
            await SetBasketModelAsync();

            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var address = new Address("123 Main St.", "Kent", "OH", "United States", "44240");

            var updateModel = items.ToDictionary(b => b.Id.ToString(), b => b.Quantity);
            await _basketService.SetQuantities(BasketModel.Id, updateModel);
            var createdOrder = await _orderService.CreateOrderAsync(BasketModel.Id, address);
            await _basketService.DeleteBasketAsync(BasketModel.Id);

            await ReserveOrder(createdOrder);
            await SaveOrderDetails(createdOrder);
        }
        catch (EmptyBasketOnCheckoutException emptyBasketOnCheckoutException)
        {
            //Redirect to Empty Basket page
            _logger.LogWarning(emptyBasketOnCheckoutException.Message);
            return RedirectToPage("/Basket/Index");
        }

        return RedirectToPage("Success");
    }

    private static async Task ReserveOrder(ApplicationCore.Entities.OrderAggregate.Order createdOrder)
    {
        const string ServiceBusConnectionString = "Endpoint=sb://aibulat.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=mjzHjdFMmlD3Wce8lD6OM4EpxwFHyEZuu+ASbHIvDpo=";
        const string QueueName = "orderqueue";

        await using var client = new ServiceBusClient(ServiceBusConnectionString);
        await using ServiceBusSender sender = client.CreateSender(QueueName);
        var orderReserve = new OrderRecord
        {
            Id = Guid.NewGuid(),
            OrderItems = createdOrder.OrderItems,
            Address = createdOrder.ShipToAddress.ToString(),
            FinalPrice = createdOrder.Total()
        };

        try
        {
            var content = new BinaryData(JsonConvert.SerializeObject(orderReserve));
            var message = new ServiceBusMessage(content);
            Console.WriteLine($"Sending message");
            await sender.SendMessageAsync(message);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"{DateTime.Now} :: Exception: {exception.Message}");
        }
        finally
        { 
            await sender.DisposeAsync();
            await client.DisposeAsync();
        }
    }

    private static async Task SaveOrderDetails(ApplicationCore.Entities.OrderAggregate.Order order, CancellationToken token = default)
    {
        var client = new HttpClient();

        var orderRecord = new OrderRecord
        {
            Id = Guid.NewGuid(),
            OrderItems = order.OrderItems,
            Address = order.ShipToAddress.ToString(),
            FinalPrice = order.Total()
        };

        using var content = new StringContent(JsonConvert.SerializeObject(orderRecord), System.Text.Encoding.UTF8, "application/json");
        
        await client.PostAsync(OrderDetailsFunctionUrl, content, token);
    }

    private async Task SetBasketModelAsync()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        if (_signInManager.IsSignedIn(HttpContext.User))
        {
            BasketModel = await _basketViewModelService.GetOrCreateBasketForUser(User.Identity.Name);
        }
        else
        {
            GetOrSetBasketCookieAndUserName();
            BasketModel = await _basketViewModelService.GetOrCreateBasketForUser(_username!);
        }
    }

    private void GetOrSetBasketCookieAndUserName()
    {
        if (Request.Cookies.ContainsKey(Constants.BASKET_COOKIENAME))
        {
            _username = Request.Cookies[Constants.BASKET_COOKIENAME];
        }
        if (_username != null) return;

        _username = Guid.NewGuid().ToString();
        var cookieOptions = new CookieOptions();
        cookieOptions.Expires = DateTime.Today.AddYears(10);
        Response.Cookies.Append(Constants.BASKET_COOKIENAME, _username, cookieOptions);
    }
}
