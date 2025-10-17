﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Helpers;
using KamPay.Models;

namespace KamPay.Services
{
    public interface IProductService
    {
        Task<ServiceResult<Product>> AddProductAsync(ProductRequest request, User currentUser);
        Task<ServiceResult<Product>> UpdateProductAsync(string productId, ProductRequest request);
        Task<ServiceResult<bool>> DeleteProductAsync(string productId);
        Task<ServiceResult<Product>> GetProductByIdAsync(string productId);
        Task<ServiceResult<List<Product>>> GetAllProductsAsync(ProductFilter filter = null);
        Task<ServiceResult<List<Product>>> GetUserProductsAsync(string userId);
        Task<ServiceResult<bool>> MarkAsSoldAsync(string productId);
        Task<ServiceResult<bool>> MarkAsReservedAsync(string productId, bool isReserved);
        Task<ServiceResult<bool>> IncrementViewCountAsync(string productId);
        Task<ServiceResult<List<Category>>> GetCategoriesAsync();
        ValidationResult ValidateProduct(ProductRequest request);
        Task<ServiceResult<List<Product>>> GetProductsAsync(string categoryId = null, string searchText = null);
        Task<ServiceResult<bool>> UpdateProductOwnerAsync(string productId, string newOwnerId, bool markAsSold = true);
    }

  

}