# NuvexaRelationships

Unencrypted demo `.nvx` with **multi-layer relationships**. Generate it with:

```bash
dotnet run --project samples/Relationships
```

Default path: `~/Downloads/NuvexaRelationships.nvx`. Open it in Nuvexa Data Studio (no key).

## Layers

```
regions
  └─ cities.regionId
       └─ warehouses.cityId
            └─ inventory.warehouseId + inventory.productId

categories
  └─ products.categoryId          (products.specs is embedded)

customers.cityId → cities
  └─ orders.customerId + orders.warehouseId
       ├─ order_items.orderId + order_items.productId
       ├─ payments.orderId
       └─ shipments.orderId       (shipments.events is embedded)
```

There are no SQL foreign keys. Ids are ordinary fields; `$lookup` resolves them at query time.

## NQL

Orders + customers (2 layers):

```javascript
db.orders.aggregate([{ $lookup: { from: "customers", localField: "customerId", foreignField: "_id", as: "customer" } }])
```

Line items + order + product (3 layers, two lookups):

```javascript
db.order_items.aggregate([
  { $lookup: { from: "orders", localField: "orderId", foreignField: "_id", as: "order" } },
  { $lookup: { from: "products", localField: "productId", foreignField: "_id", as: "product" } }
])
```

Stock + warehouse + product:

```javascript
db.inventory.aggregate([
  { $lookup: { from: "warehouses", localField: "warehouseId", foreignField: "_id", as: "warehouse" } },
  { $lookup: { from: "products", localField: "productId", foreignField: "_id", as: "product" } }
])
```
