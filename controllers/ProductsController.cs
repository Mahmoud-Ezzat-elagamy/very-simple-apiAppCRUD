using Microsoft.AspNetCore.Mvc;

using System.Collections.Generic;

namespace MyFirstApi.Controllers
{
  [ApiController]
  [Route("api/[controller]")]
  public class ProductsController : ControllerBase

  {
    List<Product> products = new List<Product>
    {
      new Product { Id = 1, Name = "Product 1", Price = 10.99m },
      new Product { Id = 2, Name = "Product 2", Price = 19.99m },
      new Product { Id = 3, Name = "Product 3", Price = 5.99m }
    };

    [HttpGet]
    public ActionResult<IEnumerable<Product>> GetAllProducts()
    {
      return Ok(products);
    }

    [HttpGet("{id}")]
    public ActionResult<Product> GetProductById(int id)
    {
      var product = products.Find(p => p.Id == id);
      if (product == null)
      {
        return NotFound();
      }
      return Ok(product);
    }

    [HttpPost]
    public ActionResult<Product> CreateProduct(Product product)
    {
      products.Add(product);
      return Created($"/api/products/{product.Id}", product);
    }

    [HttpPut("{id}")]
    public ActionResult<Product> UpdateProduct(int id, Product product)
    {
      var existingProduct = products.Find(p => p.Id == id);
      if (existingProduct == null)
      {
        return NotFound();
      }
      existingProduct.Name = product.Name;
      existingProduct.Price = product.Price;
      return Ok(products);
    }

  }
}