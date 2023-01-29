using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;
public class OrderRecord
{
    public Guid Id { get; set; }

    public IEnumerable<OrderItem> OrderItems { get; set; }

    public string Address { get; set; }

    public decimal FinalPrice { get; set; }
}
