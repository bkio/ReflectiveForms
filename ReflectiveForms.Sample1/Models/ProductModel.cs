// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using CrossCloudKit.Utilities.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Operation;
using Range = ReflectiveForms.Core.Attributes.Fields.Range;

namespace ReflectiveForms.Sample1.Models;

/// <summary>
/// Product specification row – used in a Repeater.
/// </summary>
internal class ProductSpecModel : BaseModel
{
    [JsonProperty("spec_name"),
     Text(
         label: "Specification",
         instructions: "",
         mandatory: true,
         placeholderText: "e.g. Weight, Dimensions, Material")]
    public string SpecName = "";

    [JsonProperty("spec_value"),
     Text(
         label: "Value",
         instructions: "",
         mandatory: true,
         placeholderText: "e.g. 2.5 kg, 30×20×10 cm")]
    public string SpecValue = "";
}

/// <summary>
/// Product variant – demonstrates Repeater with nested Group.
/// </summary>
internal class ProductVariantModel : BaseModel
{
    [JsonProperty("variant_name"),
     Text(
         label: "Variant Name",
         instructions: "",
         mandatory: true,
         placeholderText: "e.g. Large / Blue")]
    public string VariantName = "";

    [JsonProperty("sku"),
     Text(
         label: "SKU",
         instructions: "Stock keeping unit – must be unique.",
         mandatory: true,
         placeholderText: "e.g. PROD-001-LG-BLU")]
    public string Sku = "";

    [JsonProperty("price"),
     Number(
         label: "Price (USD)",
         instructions: "",
         mandatory: true,
         placeholderText: "e.g. 29.99",
         minimumMaximumValues: [0, 999999],
         stepSize: 0.01)]
    public double Price;

    [JsonProperty("stock_quantity"),
     Number(
         label: "Stock Quantity",
         instructions: "",
         mandatory: true,
         placeholderText: "e.g. 100",
         defaultValue: 0,
         minimumMaximumValues: [0, 1000000])]
    public double StockQuantity;

    [JsonProperty("is_available"),
     Checkbox(
         label: "Available for Sale",
         instructions: "",
         defaultValue: true)]
    public bool IsAvailable;
}

/// <summary>
/// Product gallery image – used in a Repeater.
/// </summary>
internal class GalleryImageModel : BaseModel
{
    [JsonProperty("image"),
     MediaSourceBase64(
         label: "Image",
         instructions: "Upload a product image.",
         mandatory: true)]
    public string Image = "";

    [JsonProperty("caption"),
     Text(
         label: "Caption",
         instructions: "",
         mandatory: false,
         placeholderText: "Image description")]
    public string Caption = "";

    [JsonProperty("sort_order"),
     Number(
         label: "Sort Order",
         instructions: "Lower numbers appear first.",
         mandatory: false,
         placeholderText: "0",
         defaultValue: 0,
         minimumMaximumValues: [0, 100])]
    public double SortOrder;
}

/// <summary>
/// Product entity – demonstrates:
/// - Complete e-commerce product model
/// - DynamicChoicesRuntimeAsync (category-dependent subcategory)
/// - Multiple Repeaters (variants, specs, gallery)
/// - Group with Grid4ElementsInRow (shipping dimensions)
/// - Number with various configurations (stepSize, min/max, defaults)
/// - LogicSanityCheckAsync for SKU uniqueness
/// - Relation to team-member entity
/// - WysiwygEditor for long description
/// - TextArea for short description
/// - Checkbox flags
/// - DatePicker with default value
/// </summary>
internal class ProductModel : EntityFieldsModel
{
    [JsonProperty("short_description"),
     TextArea(
         label: "Short Description",
         instructions: "Appears in product cards and search results.",
         mandatory: true,
         placeholderText: "A brief one-liner about this product...")]
    public string ShortDescription = "";

    [JsonProperty("long_description"),
     WysiwygEditor(
         label: "Full Description",
         instructions: "Detailed product description with rich formatting.",
         mandatory: false)]
    public string LongDescription = "";

    [JsonProperty("product_category"),
     Select(
         label: "Product Category",
         instructions: "Primary category for this product.",
         defaultValue: "electronics",
         choices:
         [
             "electronics : Electronics",
             "clothing : Clothing & Apparel",
             "home : Home & Garden",
             "sports : Sports & Outdoors",
             "books : Books & Media",
             "food : Food & Beverages"
         ])]
    public string ProductCategory = "electronics";

    [JsonProperty("subcategory"),
     Select(
         label: "Subcategory",
         instructions: "Choose a subcategory based on the product category above.",
         defaultValue: "",
         choices: null)]
    public string Subcategory = "";
    public Task<string> Subcategory___DynamicChoicesRuntimeAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult("""
                               const input = window.latest_dynamic_options_input;
                               const cat = input.product_category;

                               if (cat === 'electronics') return [
                                  ' : Select Subcategory',
                                  'phones : Phones & Tablets',
                                  'laptops : Laptops & Computers',
                                  'audio : Audio & Headphones',
                                  'cameras : Cameras & Photography',
                                  'accessories : Accessories'
                               ];
                               if (cat === 'clothing') return [
                                  ' : Select Subcategory',
                                  'mens : Men\'s',
                                  'womens : Women\'s',
                                  'kids : Kids',
                                  'shoes : Shoes',
                                  'accessories_cl : Accessories'
                               ];
                               if (cat === 'home') return [
                                  ' : Select Subcategory',
                                  'furniture : Furniture',
                                  'kitchen : Kitchen',
                                  'garden : Garden',
                                  'decor : Decor',
                                  'tools : Tools'
                               ];
                               if (cat === 'sports') return [
                                  ' : Select Subcategory',
                                  'fitness : Fitness',
                                  'outdoor : Outdoor Recreation',
                                  'team_sports : Team Sports',
                                  'water_sports : Water Sports'
                               ];
                               if (cat === 'books') return [
                                  ' : Select Subcategory',
                                  'fiction : Fiction',
                                  'nonfiction : Non-Fiction',
                                  'educational : Educational',
                                  'comics : Comics & Manga'
                               ];
                               if (cat === 'food') return [
                                  ' : Select Subcategory',
                                  'organic : Organic',
                                  'snacks : Snacks',
                                  'beverages : Beverages',
                                  'supplements : Supplements'
                               ];

                               return [' : Select a category first'];
                               """);
    }

    [JsonProperty("primary_image"),
     MediaSourceBase64(
         label: "Primary Product Image",
         instructions: "Main image shown in listing and detail pages.",
         mandatory: true)]
    public string PrimaryImage = "";

    [JsonProperty("gallery", NullValueHandling = NullValueHandling.Ignore),
     Repeater(
         label: "Image Gallery",
         instructions: "Additional product images.",
         repeaterFor: typeof(GalleryImageModel),
         addButtonLabel: "Add Gallery Image",
         minimumRows: 0,
         maximumRows: 20,
         useAccordion: RepeatUseAccordion.Yes)]
    public List<GalleryImageModel> Gallery = [];

    [JsonProperty("base_price"),
     Number(
         label: "Base Price (USD)",
         instructions: "Starting price before variant adjustments.",
         mandatory: true,
         placeholderText: "e.g. 49.99",
         minimumMaximumValues: [0, 999999],
         stepSize: 0.01)]
    public double BasePrice;

    [JsonProperty("discount_percentage"),
     Range(
         label: "Discount Percentage",
         instructions: "Active discount on this product.",
         mandatory: false,
         defaultValue: 0,
         minimumValue: 0,
         maximumValue: 90,
         stepSize: 5)]
    public double DiscountPercentage;

    [JsonProperty("is_published"),
     Checkbox(
         label: "Published",
         instructions: "Unpublished products are only visible to administrators.",
         defaultValue: false)]
    public bool IsPublished;

    [JsonProperty("is_digital"),
     Checkbox(
         label: "Digital Product",
         instructions: "Check if this product is delivered digitally (no shipping).",
         defaultValue: false)]
    public bool IsDigital;

    [JsonProperty("weight_kg"),
     DisplayCondition("is_digital == false"),
     Number(
         label: "Weight (kg)",
         instructions: "Product weight for shipping calculation.",
         mandatory: false,
         placeholderText: "e.g. 1.5",
         minimumMaximumValues: [0, 1000],
         stepSize: 0.1)]
    public double WeightKg;

    [JsonProperty("variants", NullValueHandling = NullValueHandling.Ignore),
     Repeater(
         label: "Product Variants",
         instructions: "Define size/color/style variants with individual pricing and stock.",
         repeaterFor: typeof(ProductVariantModel),
         addButtonLabel: "Add Variant",
         minimumRows: 1,
         maximumRows: 50,
         groupRenderStyle: GroupRenderStyle.Grid2ElementsInRow,
         useAccordion: RepeatUseAccordion.Yes)]
    public List<ProductVariantModel> Variants = [];

    [JsonProperty("specifications", NullValueHandling = NullValueHandling.Ignore),
     Repeater(
         label: "Specifications",
         instructions: "Technical specifications displayed in a table format.",
         repeaterFor: typeof(ProductSpecModel),
         addButtonLabel: "Add Specification",
         groupRenderStyle: GroupRenderStyle.Grid2ElementsInRow,
         useAccordion: RepeatUseAccordion.No)]
    public List<ProductSpecModel> Specifications = [];

    [JsonProperty("product_manager"),
     Relation(
         label: "Product Manager",
         instructions: "The team member responsible for this product.",
         mandatory: false,
         relationEntityName: "team-member",
         isRelationEntityNotExistsOk: true)]
    public int ProductManagerId = -1;

    [JsonProperty("launch_date"),
     DatePicker(
         label: "Launch Date",
         instructions: "When this product was or will be launched.",
         mandatory: false,
         dateFormat: "yyyyMMdd")]
    public string LaunchDate = "";

    [JsonProperty("product_url"),
     Url(
         label: "External Product Page",
         instructions: "Link to the manufacturer's product page.",
         mandatory: false,
         placeholderText: "https://manufacturer.com/product")]
    public string ProductUrl = "";
}
