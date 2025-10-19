using Firebase.Database;
using Firebase.Database.Query;
using KamPay.Helpers;
using KamPay.Models;
using System.Linq;

namespace KamPay.Services;
public class FirebaseProductService : IProductService
{
    private readonly FirebaseClient _firebaseClient;
    private readonly IStorageService _storageService;

    public FirebaseProductService(IStorageService storageService)
    {
        _firebaseClient = new FirebaseClient(Constants.FirebaseRealtimeDbUrl);
        _storageService = storageService;
    }

    public async Task<ServiceResult<List<Product>>> GetAllProductsAsync(ProductFilter filter = null)
    {
        try
        {
            var allProducts = await _firebaseClient
                .Child(Constants.ProductsCollection)
                .OnceAsync<Product>();

            var productsQuery = allProducts.Select(p =>
            {
                var product = p.Object;
                product.ProductId = p.Key;
                return product;
            }).AsQueryable(); // Sorgulanabilir hale getiriyoruz

            // Filtreleme
            if (filter != null)
            {
                // Sadece aktif �r�nler
                if (filter.OnlyActive)
                {
                    productsQuery = productsQuery.Where(p => p.IsActive);
                }

                // SATILMI� �R�NLER F�LTRES�N� DEVRE DI�I BIRAKTIK
                /*
                if (filter.ExcludeSold)
                {
                    productsQuery = productsQuery.Where(p => !p.IsSold);
                }
                */

                // Arama metni
                if (!string.IsNullOrWhiteSpace(filter.SearchText))
                {
                    var searchLower = filter.SearchText.ToLower();
                    productsQuery = productsQuery.Where(p =>
                        p.Title.ToLower().Contains(searchLower) ||
                        p.Description.ToLower().Contains(searchLower)
                    );
                }

                // Kategori
                if (!string.IsNullOrWhiteSpace(filter.CategoryId))
                {
                    productsQuery = productsQuery.Where(p => p.CategoryId == filter.CategoryId);
                }

                // Tip
                if (filter.Type.HasValue)
                {
                    productsQuery = productsQuery.Where(p => p.Type == filter.Type.Value);
                }

                // Durum
                if (filter.Condition.HasValue)
                {
                    productsQuery = productsQuery.Where(p => p.Condition == filter.Condition.Value);
                }

                // Fiyat aral���
                if (filter.MinPrice.HasValue)
                {
                    productsQuery = productsQuery.Where(p => p.Price >= filter.MinPrice.Value);
                }
                if (filter.MaxPrice.HasValue)
                {
                    productsQuery = productsQuery.Where(p => p.Price <= filter.MaxPrice.Value);
                }

                // Konum
                if (!string.IsNullOrWhiteSpace(filter.Location))
                {
                    var locationLower = filter.Location.ToLower();
                    productsQuery = productsQuery.Where(p =>
                        p.Location != null && p.Location.ToLower().Contains(locationLower)
                    );
                }

                // S�ralama
                productsQuery = filter.SortBy switch
                {
                    ProductSortOption.Newest => productsQuery.OrderByDescending(p => p.CreatedAt),
                    ProductSortOption.Oldest => productsQuery.OrderBy(p => p.CreatedAt),
                    ProductSortOption.PriceAsc => productsQuery.OrderBy(p => p.Price),
                    ProductSortOption.PriceDesc => productsQuery.OrderByDescending(p => p.Price),
                    ProductSortOption.MostViewed => productsQuery.OrderByDescending(p => p.ViewCount),
                    ProductSortOption.MostFavorited => productsQuery.OrderByDescending(p => p.FavoriteCount),
                    _ => productsQuery.OrderByDescending(p => p.CreatedAt)
                };
            }
            else
            {
                // Filtre yoksa varsay�lan s�ralama
                productsQuery = productsQuery.OrderByDescending(p => p.CreatedAt);
            }

            var products = productsQuery.ToList();
            return ServiceResult<List<Product>>.SuccessResult(products);
        }
        catch (Exception ex)
        {
            return ServiceResult<List<Product>>.FailureResult("�r�nler y�klenemedi", ex.Message);
        }
    }

    // ... BU DOSYADAK� D��ER T�M METOTLARINIZ (AddProductAsync, UpdateProductAsync vb.) AYNI KALACAK ...
    #region Di�er Metotlar
    public async Task<ServiceResult<Product>> AddProductAsync(ProductRequest request, User currentUser)
    {
        try
        {
            var validation = ValidateProduct(request);
            if (!validation.IsValid)
            {
                return ServiceResult<Product>.FailureResult("�r�n bilgileri ge�ersiz", validation.Errors.ToArray());
            }
            // Kategori ad�n� �nceden alal�m
            var categories = await GetCategoriesAsync();
            var categoryName = categories.Data?.FirstOrDefault(c => c.CategoryId == request.CategoryId)?.Name ?? "Bilinmeyen";

            var product = new Product
            {
                ProductId = Guid.NewGuid().ToString(), // ID'yi burada olu�turmak daha g�venli
                Title = request.Title.Trim(),
                Description = request.Description.Trim(),
                CategoryId = request.CategoryId,
                CategoryName = categoryName,
                Condition = request.Condition,
                Type = request.Type,
                Price = request.Price,
                Location = request.Location?.Trim(),
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                UserId = currentUser.UserId,
                UserName = currentUser.FullName,
                UserEmail = currentUser.Email,
                UserPhotoUrl = currentUser.ProfileImageUrl,
                ExchangePreference = request.ExchangePreference?.Trim(),
                // --- EKS�K SATIRI BURAYA EKLEY�N ---
                IsForSurpriseBox = request.IsForSurpriseBox,

                // Durum bilgilerini ayarl�yoruz
                IsActive = true,
                IsSold = false,
                IsReserved = false,
                CreatedAt = DateTime.UtcNow
            };



            // --- RES�M Y�KLEME KODU ---
            if (request.ImagePaths != null && request.ImagePaths.Any())
            {
                var imageUrls = new List<string>();
                for (int i = 0; i < Math.Min(request.ImagePaths.Count, Constants.MaxProductImages); i++)
                {
                    var uploadResult = await _storageService.UploadProductImageAsync(request.ImagePaths[i], product.ProductId, i);
                    if (uploadResult.Success)
                    {
                        imageUrls.Add(uploadResult.Data);
                    }
                }
                product.ImageUrls = imageUrls;
                if (imageUrls.Any())
                {
                    product.ThumbnailUrl = imageUrls.First();
                }
            }

            // �r�n� veritaban�na kaydet
            await _firebaseClient
                .Child(Constants.ProductsCollection)
                .Child(product.ProductId)
                .PutAsync(product);

            return ServiceResult<Product>.SuccessResult(product, "�r�n ba�ar�yla eklendi!");
        }
        catch (Exception ex)
        {
            return ServiceResult<Product>.FailureResult("�r�n eklenirken hata olu�tu", ex.Message);
        }
    }

    public async Task<ServiceResult<Product>> UpdateProductAsync(string productId, ProductRequest request)
    {
        try
        {
            var validation = ValidateProduct(request);
            if (!validation.IsValid)
            {
                return ServiceResult<Product>.FailureResult("�r�n bilgileri ge�ersiz", validation.Errors.ToArray());
            }

            var existingProduct = await _firebaseClient
                .Child(Constants.ProductsCollection)
                .Child(productId)
                .OnceSingleAsync<Product>();

            if (existingProduct == null)
            {
                return ServiceResult<Product>.FailureResult("�r�n bulunamad�");
            }

            existingProduct.Title = request.Title.Trim();
            existingProduct.Description = request.Description.Trim();
            existingProduct.CategoryId = request.CategoryId;
            existingProduct.Condition = request.Condition;
            existingProduct.Type = request.Type;
            existingProduct.Price = request.Price;
            existingProduct.Location = request.Location?.Trim();
            existingProduct.Latitude = request.Latitude;
            existingProduct.Longitude = request.Longitude;
            existingProduct.ExchangePreference = request.ExchangePreference?.Trim();
            existingProduct.UpdatedAt = DateTime.UtcNow;

            var categories = await GetCategoriesAsync();
            var category = categories.Data?.FirstOrDefault(c => c.CategoryId == request.CategoryId);
            if (category != null)
            {
                existingProduct.CategoryName = category.Name;
            }

            if (request.ImagePaths != null && request.ImagePaths.Any())
            {
                var newImageUrls = new List<string>();
                for (int i = 0; i < Math.Min(request.ImagePaths.Count, Constants.MaxProductImages); i++)
                {
                    var uploadResult = await _storageService.UploadProductImageAsync(request.ImagePaths[i], productId, i);
                    if (uploadResult.Success)
                    {
                        newImageUrls.Add(uploadResult.Data);
                    }
                }
                existingProduct.ImageUrls = newImageUrls;
                if (newImageUrls.Any())
                {
                    existingProduct.ThumbnailUrl = newImageUrls.First();
                }
            }

            await _firebaseClient
                .Child(Constants.ProductsCollection)
                .Child(productId)
                .PutAsync(existingProduct);

            return ServiceResult<Product>.SuccessResult(existingProduct, "�r�n g�ncellendi");
        }
        catch (Exception ex)
        {
            return ServiceResult<Product>.FailureResult("G�ncelleme hatas�", ex.Message);
        }
    }

    public async Task<ServiceResult<bool>> DeleteProductAsync(string productId)
    {
        try
        {
            var product = await _firebaseClient
                .Child(Constants.ProductsCollection)
                .Child(productId)
                .OnceSingleAsync<Product>();

            if (product == null)
            {
                return ServiceResult<bool>.FailureResult("�r�n bulunamad�");
            }

            if (product.ImageUrls != null && product.ImageUrls.Any())
            {
                foreach (var imageUrl in product.ImageUrls)
                {
                    await _storageService.DeleteImageAsync(imageUrl);
                }
            }

            var allFavorites = await _firebaseClient
                .Child(Constants.FavoritesCollection)
                .OrderBy("ProductId")
                .EqualTo(productId)
                .OnceAsync<Favorite>();

            if (allFavorites.Any())
            {
                foreach (var favoriteEntry in allFavorites)
                {
                    await _firebaseClient
                        .Child(Constants.FavoritesCollection)
                        .Child(favoriteEntry.Key)
                        .DeleteAsync();
                }
            }

            await _firebaseClient
                .Child(Constants.ProductsCollection)
                .Child(productId)
                .DeleteAsync();

            return ServiceResult<bool>.SuccessResult(true, "�r�n ve ili�kili favoriler silindi");
        }
        catch (Exception ex)
        {
            return ServiceResult<bool>.FailureResult("Silme hatas�", ex.Message);
        }
    }

    public async Task<ServiceResult<Product>> GetProductByIdAsync(string productId)
    {
        try
        {
            var product = await _firebaseClient
                .Child(Constants.ProductsCollection)
                .Child(productId)
                .OnceSingleAsync<Product>();

            if (product == null)
            {
                return ServiceResult<Product>.FailureResult("�r�n bulunamad�");
            }

            return ServiceResult<Product>.SuccessResult(product);
        }
        catch (Exception ex)
        {
            return ServiceResult<Product>.FailureResult("Hata olu�tu", ex.Message);
        }
    }

    public async Task<ServiceResult<List<Product>>> GetUserProductsAsync(string userId)
    {
        try
        {
            var allProducts = await _firebaseClient
                .Child(Constants.ProductsCollection)
                .OrderBy("UserId")
                .EqualTo(userId)
                .OnceAsync<Product>();

            foreach (var product in allProducts)
            {
                product.Object.ProductId = product.Key;
            }

            var products = allProducts
                .Select(p => p.Object)
                .Where(p => p.IsActive && !p.IsSold && !p.IsReserved)
                .OrderByDescending(p => p.CreatedAt)
                .ToList();

            return ServiceResult<List<Product>>.SuccessResult(products);
        }
        catch (Exception ex)
        {
            return ServiceResult<List<Product>>.FailureResult("Kullan�c�n�n �r�nleri al�namad�.", ex.Message);
        }
    }
    public async Task<ServiceResult<bool>> MarkAsSoldAsync(string productId)
    {
        try
        {
            var product = await _firebaseClient
                .Child(Constants.ProductsCollection)
                .Child(productId)
                .OnceSingleAsync<Product>();

            if (product == null)
            {
                return ServiceResult<bool>.FailureResult("�r�n bulunamad�");
            }

            product.IsSold = true;
            product.SoldAt = DateTime.UtcNow;
            product.IsActive = false;

            await _firebaseClient
                .Child(Constants.ProductsCollection)
                .Child(productId)
                .PutAsync(product);

            return ServiceResult<bool>.SuccessResult(true, "�r�n sat�ld� olarak i�aretlendi");
        }
        catch (Exception ex)
        {
            return ServiceResult<bool>.FailureResult("��lem ba�ar�s�z", ex.Message);
        }
    }

    public async Task<ServiceResult<bool>> MarkAsReservedAsync(string productId, bool isReserved)
    {
        try
        {
            var product = await _firebaseClient
                .Child(Constants.ProductsCollection)
                .Child(productId)
                .OnceSingleAsync<Product>();

            if (product == null)
            {
                return ServiceResult<bool>.FailureResult("�r�n bulunamad�");
            }

            product.IsReserved = isReserved;

            await _firebaseClient
                .Child(Constants.ProductsCollection)
                .Child(productId)
                .PutAsync(product);

            var message = isReserved ? "�r�n rezerve edildi" : "Rezervasyon kald�r�ld�";
            return ServiceResult<bool>.SuccessResult(true, message);
        }
        catch (Exception ex)
        {
            return ServiceResult<bool>.FailureResult("��lem ba�ar�s�z", ex.Message);
        }
    }

    public async Task<ServiceResult<bool>> IncrementViewCountAsync(string productId)
    {
        try
        {
            var product = await _firebaseClient
                .Child(Constants.ProductsCollection)
                .Child(productId)
                .OnceSingleAsync<Product>();

            if (product == null)
            {
                return ServiceResult<bool>.FailureResult("�r�n bulunamad�");
            }

            product.ViewCount++;

            await _firebaseClient
                .Child(Constants.ProductsCollection)
                .Child(productId)
                .PutAsync(product);

            return ServiceResult<bool>.SuccessResult(true);
        }
        catch (Exception ex)
        {
            return ServiceResult<bool>.FailureResult("Hata", ex.Message);
        }
    }

    public async Task<ServiceResult<List<Category>>> GetCategoriesAsync()
    {
        try
        {
            var firebaseCategories = await _firebaseClient
                .Child(Constants.CategoriesCollection)
                .OnceAsync<Category>();

            if (firebaseCategories.Any())
            {
                // D�ZELTME: Firebase'den gelen 'Key'i, nesnenin 'CategoryId' �zelli�ine at�yoruz.
                var categories = firebaseCategories.Select(c =>
                {
                    var category = c.Object;
                    category.CategoryId = c.Key; // En �nemli sat�r!
                    return category;
                }).ToList();

                return ServiceResult<List<Category>>.SuccessResult(categories);
            }

            // --- Tohumlama (Seeding) Mant���n� da �yile�tirelim ---
            var defaultCategories = Category.GetDefaultCategories();
            foreach (var category in defaultCategories)
            {
                // PutAsync yerine PostAsync kullanarak Firebase'in benzersiz ID olu�turmas�n� sa�layal�m.
                // Bu, yukar�daki veri �ekme mant���yla %100 uyumlu �al���r.
                await _firebaseClient
                    .Child(Constants.CategoriesCollection)
                    .PostAsync(category);
            }

            // Veritaban� bo�sa ve yeni doldurulduysa, tekrar okuyarak do�ru ID'lerle d�nelim
            return await GetCategoriesAsync();
        }
        catch (Exception ex)
        {
            return ServiceResult<List<Category>>.SuccessResult(
                Category.GetDefaultCategories(),
                "Kategoriler yerel olarak y�klendi"
            );
        }
    }

    public ValidationResult ValidateProduct(ProductRequest request)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            result.AddError("�r�n ba�l��� bo� olamaz");
        }
        else if (request.Title.Length > Constants.MaxProductTitleLength)
        {
            result.AddError($"Ba�l�k en fazla {Constants.MaxProductTitleLength} karakter olabilir");
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            result.AddError("�r�n a��klamas� bo� olamaz");
        }
        else if (request.Description.Length > Constants.MaxProductDescriptionLength)
        {
            result.AddError($"A��klama en fazla {Constants.MaxProductDescriptionLength} karakter olabilir");
        }

        if (string.IsNullOrWhiteSpace(request.CategoryId))
        {
            result.AddError("Kategori se�ilmelidir");
        }

        if (request.Type == ProductType.Satis)
        {
            if (request.Price <= 0)
            {
                result.AddError("Sat�� fiyat� 0'dan b�y�k olmal�d�r");
            }
            else if (request.Price > 999999)
            {
                result.AddError("Fiyat �ok y�ksek");
            }
        }

        if (request.ImagePaths != null && request.ImagePaths.Count > Constants.MaxProductImages)
        {
            result.AddError($"En fazla {Constants.MaxProductImages} g�rsel eklenebilir");
        }

        if (request.Type == ProductType.Takas && string.IsNullOrWhiteSpace(request.ExchangePreference))
        {
            result.AddError("Takas i�in tercih belirtilmelidir");
        }

        return result;
    }
    #endregion
    // ... (Mevcut metotlar�n�z�n sonu) ...

    // --- YEN� EKLENEN METOT 1 ---
    public async Task<ServiceResult<List<Product>>> GetProductsAsync(string categoryId = null, string searchText = null)
    {
        try
        {
            var query = _firebaseClient.Child(Constants.ProductsCollection).OrderBy("CreatedAt");
            var productItems = await query.OnceAsync<Product>();

            var products = productItems.Select(item =>
            {
                var product = item.Object;
                product.ProductId = item.Key;
                return product;
            })
            .Where(p => !p.IsSold) // Sadece sat�lmam�� olanlar� getir
            .OrderByDescending(p => p.CreatedAt)
            .ToList();

            // Kategoriye g�re filtrele
            if (!string.IsNullOrEmpty(categoryId))
            {
                products = products.Where(p => p.CategoryId == categoryId).ToList();
            }

            // Arama metnine g�re filtrele
            if (!string.IsNullOrEmpty(searchText))
            {
                products = products.Where(p => p.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return ServiceResult<List<Product>>.SuccessResult(products);
        }
        catch (Exception ex)
        {
            return ServiceResult<List<Product>>.FailureResult("Hata", ex.Message);
        }
    }

    // --- YEN� EKLENEN METOT 2 ---
    public async Task<ServiceResult<bool>> UpdateProductOwnerAsync(string productId, string newOwnerId, bool markAsSold = true)
    {
        try
        {
            var productNode = _firebaseClient.Child(Constants.ProductsCollection).Child(productId);
            var product = await productNode.OnceSingleAsync<Product>();

            if (product == null)
            {
                return ServiceResult<bool>.FailureResult("�r�n bulunamad�.");
            }

            product.UserId = newOwnerId; // Yeni sahibi ata
            if (markAsSold)
            {
                // �r�n� hem sat�ld� hem de rezerve de�il olarak i�aretle
                product.IsSold = true;
                product.IsReserved = false;
            }

            await productNode.PutAsync(product);
            return ServiceResult<bool>.SuccessResult(true, "�r�n sahibi g�ncellendi.");
        }
        catch (Exception ex)
        {
            return ServiceResult<bool>.FailureResult("Hata", ex.Message);
        }
    }




}