# C# Best Practices for AI Agents

**Version 1.0.0**  
Enterprise Engineering  
January 2026

> **Note:**  
> This document is designed for AI agents and LLMs to follow when maintaining,  
> generating, or refactoring C# and .NET codebases. Humans may also find it useful,  
> but guidance here is optimized for automation and consistency by AI-assisted workflows.

---

## Abstract

Comprehensive performance optimization guide for C# and .NET applications, designed for AI agents and LLMs. Contains 45+ rules across 9 categories, prioritized by impact from critical (async/await patterns, memory allocation) to incremental (advanced patterns). Each rule includes detailed explanations, real-world examples comparing incorrect vs. correct implementations, and specific impact metrics to guide automated refactoring and code generation.

---

## Table of Contents

1. [Async/Await Patterns](#1-asyncawait-patterns) — **CRITICAL**
2. [Memory Management](#2-memory-management) — **CRITICAL**
3. [Collection Performance](#3-collection-performance) — **HIGH**
4. [Database & EF Core](#4-database--ef-core) — **HIGH**
5. [LINQ Optimization](#5-linq-optimization) — **MEDIUM-HIGH**
6. [String Operations](#6-string-operations) — **MEDIUM**
7. [Serialization](#7-serialization) — **MEDIUM**
8. [Concurrency & Threading](#8-concurrency--threading) — **MEDIUM**
9. [Advanced Patterns](#9-advanced-patterns) — **LOW-MEDIUM**

---

## 1. Async/Await Patterns

**Impact: CRITICAL**

Improper async/await usage is the #1 performance killer in .NET applications. Correct patterns eliminate thread blocking and improve scalability.

### 1.1 Avoid Async Void

**Impact: CRITICAL (prevents exception handling and tracking)**

`async void` methods cannot be awaited, making exception handling impossible and causing application crashes.

**Incorrect: unhandled exceptions crash the app**

```csharp
// ❌ Bad - exceptions cannot be caught
private async void LoadDataAsync()
{
    var data = await _httpClient.GetStringAsync("https://api.example.com/data");
    // If this throws, the app crashes
    ProcessData(data);
}

private void Button_Click(object sender, EventArgs e)
{
    LoadDataAsync(); // Fire and forget - dangerous
}
```

**Correct: use async Task**

```csharp
// ✅ Good - exceptions can be handled
private async Task LoadDataAsync()
{
    var data = await _httpClient.GetStringAsync("https://api.example.com/data");
    ProcessData(data);
}

private async void Button_Click(object sender, EventArgs e)
{
    try
    {
        await LoadDataAsync();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to load data");
    }
}
```

**Exception: Event handlers only**

`async void` is only acceptable for event handlers where the signature is dictated by the framework.

### 1.2 ConfigureAwait(false) in Libraries

**Impact: HIGH (prevents deadlocks, improves performance)**

Use `ConfigureAwait(false)` in library code to avoid capturing synchronization context and prevent deadlocks.

**Incorrect: captures context unnecessarily**

```csharp
// ❌ Bad - library code
public async Task<User> GetUserAsync(int id)
{
    var response = await _httpClient.GetAsync($"/users/{id}");
    var content = await response.Content.ReadAsStringAsync();
    return JsonSerializer.Deserialize<User>(content);
}
```

**Correct: avoids context capture**

```csharp
// ✅ Good - library code
public async Task<User> GetUserAsync(int id)
{
    var response = await _httpClient.GetAsync($"/users/{id}").ConfigureAwait(false);
    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    return JsonSerializer.Deserialize<User>(content);
}
```

**When NOT to use:**
- ASP.NET Core applications (no synchronization context)
- UI code that needs to return to UI thread
- Code that accesses HttpContext or similar context-dependent objects

### 1.3 ValueTask for Hot Paths

**Impact: MEDIUM (reduces allocations in frequently-called async methods)**

Use `ValueTask<T>` instead of `Task<T>` for methods that often complete synchronously or are called frequently.

**Incorrect: allocates Task on every call**

```csharp
// ❌ Bad - allocates Task even when data is cached
private readonly Dictionary<int, User> _cache = new();

public async Task<User> GetUserAsync(int id)
{
    if (_cache.TryGetValue(id, out var user))
    {
        return user; // Still allocates a Task
    }
    
    user = await _database.GetUserAsync(id);
    _cache[id] = user;
    return user;
}
```

**Correct: zero allocation when cached**

```csharp
// ✅ Good - no allocation when data is cached
private readonly Dictionary<int, User> _cache = new();

public ValueTask<User> GetUserAsync(int id)
{
    if (_cache.TryGetValue(id, out var user))
    {
        return new ValueTask<User>(user); // No heap allocation
    }
    
    return new ValueTask<User>(GetUserFromDatabaseAsync(id));
}

private async Task<User> GetUserFromDatabaseAsync(int id)
{
    var user = await _database.GetUserAsync(id);
    _cache[id] = user;
    return user;
}
```

**Important:** Never await a `ValueTask` twice. Convert to `Task` if needed:

```csharp
ValueTask<User> valueTask = GetUserAsync(id);
Task<User> task = valueTask.AsTask(); // Now safe to await multiple times
```

### 1.4 Parallel Execution for Independent Operations

**Impact: CRITICAL (2-10× improvement)**

Execute independent async operations in parallel using `Task.WhenAll`.

**Incorrect: sequential execution**

```csharp
// ❌ Bad - 3 sequential round trips
public async Task<Dashboard> GetDashboardAsync(int userId)
{
    var user = await _userService.GetUserAsync(userId);
    var orders = await _orderService.GetOrdersAsync(userId);
    var notifications = await _notificationService.GetNotificationsAsync(userId);
    
    return new Dashboard(user, orders, notifications);
}
```

**Correct: parallel execution**

```csharp
// ✅ Good - 1 round trip
public async Task<Dashboard> GetDashboardAsync(int userId)
{
    var userTask = _userService.GetUserAsync(userId);
    var ordersTask = _orderService.GetOrdersAsync(userId);
    var notificationsTask = _notificationService.GetNotificationsAsync(userId);
    
    await Task.WhenAll(userTask, ordersTask, notificationsTask);
    
    return new Dashboard(
        await userTask,
        await ordersTask,
        await notificationsTask
    );
}
```

**Alternative: with exception handling**

```csharp
public async Task<Dashboard> GetDashboardAsync(int userId)
{
    var tasks = new[]
    {
        _userService.GetUserAsync(userId),
        _orderService.GetOrdersAsync(userId),
        _notificationService.GetNotificationsAsync(userId)
    };
    
    try
    {
        await Task.WhenAll(tasks);
    }
    catch
    {
        // WhenAll throws on first exception, but all tasks continue
        // Check individual tasks for specific errors
    }
    
    return new Dashboard(
        await tasks[0],
        await tasks[1],
        await tasks[2]
    );
}
```

### 1.5 Avoid Task.Result and Task.Wait

**Impact: CRITICAL (prevents deadlocks)**

Never block on async code using `.Result` or `.Wait()`. This causes deadlocks in UI and ASP.NET contexts.

**Incorrect: deadlock prone**

```csharp
// ❌ Bad - causes deadlock in UI/ASP.NET
public User GetUser(int id)
{
    return GetUserAsync(id).Result; // Deadlock!
}

public void ProcessData()
{
    LoadDataAsync().Wait(); // Deadlock!
}
```

**Correct: async all the way**

```csharp
// ✅ Good - async all the way
public async Task<User> GetUserAsync(int id)
{
    return await _database.GetUserAsync(id);
}

public async Task ProcessDataAsync()
{
    await LoadDataAsync();
}
```

**Exception: Console applications**

In console apps with no synchronization context, blocking is safe but still not recommended:

```csharp
// Acceptable only in Main
static void Main(string[] args)
{
    MainAsync(args).GetAwaiter().GetResult();
}

static async Task MainAsync(string[] args)
{
    // async code here
}
```

### 1.6 Defer Await Until Needed

**Impact: MEDIUM (avoids blocking unused code paths)**

Don't await operations until the result is actually needed.

**Incorrect: blocks both branches**

```csharp
// ❌ Bad
public async Task<IActionResult> ProcessOrderAsync(int orderId, bool skipValidation)
{
    var order = await _orderService.GetOrderAsync(orderId);
    
    if (skipValidation)
    {
        return Ok(); // Still waited for order unnecessarily
    }
    
    return await ValidateAndProcessAsync(order);
}
```

**Correct: only blocks when needed**

```csharp
// ✅ Good
public async Task<IActionResult> ProcessOrderAsync(int orderId, bool skipValidation)
{
    if (skipValidation)
    {
        return Ok(); // Returns immediately
    }
    
    var order = await _orderService.GetOrderAsync(orderId);
    return await ValidateAndProcessAsync(order);
}
```

---

## 2. Memory Management

**Impact: CRITICAL**

Improper memory management causes GC pressure, allocations, and performance degradation.

### 2.1 Use ArrayPool for Temporary Buffers

**Impact: HIGH (eliminates allocations for temporary arrays)**

Rent arrays from `ArrayPool<T>` instead of allocating new ones for temporary operations.

**Incorrect: allocates new array**

```csharp
// ❌ Bad - allocates 8KB on heap every call
public byte[] ProcessData(Stream stream)
{
    byte[] buffer = new byte[8192];
    int bytesRead = stream.Read(buffer, 0, buffer.Length);
    
    // process buffer
    return result;
}
```

**Correct: rents from pool**

```csharp
// ✅ Good - reuses pooled array
public byte[] ProcessData(Stream stream)
{
    byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
    try
    {
        int bytesRead = stream.Read(buffer, 0, buffer.Length);
        
        // process buffer
        return result;
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
```

**Important:** Always return arrays to the pool in a `finally` block.

### 2.2 Use Span<T> and Memory<T>

**Impact: HIGH (zero-copy operations)**

Use `Span<T>` and `Memory<T>` to avoid allocations when slicing arrays.

**Incorrect: allocates substring**

```csharp
// ❌ Bad - allocates new string
public string GetDomain(string email)
{
    int index = email.IndexOf('@');
    return email.Substring(index + 1); // Allocates new string
}
```

**Correct: zero-allocation slice**

```csharp
// ✅ Good - no allocation
public ReadOnlySpan<char> GetDomain(ReadOnlySpan<char> email)
{
    int index = email.IndexOf('@');
    return email.Slice(index + 1); // No allocation
}

// Usage
ReadOnlySpan<char> domain = GetDomain("user@example.com");
```

**For async operations, use Memory<T>:**

```csharp
// Span<T> cannot be used in async methods
public async Task ProcessAsync(Memory<byte> data)
{
    await _stream.WriteAsync(data);
}
```

### 2.3 Struct for Small, Immutable Types

**Impact: MEDIUM (reduces heap allocations)**

Use `struct` instead of `class` for small, immutable value types that are frequently allocated.

**Incorrect: allocates on heap**

```csharp
// ❌ Bad - allocates on heap
public class Point
{
    public int X { get; set; }
    public int Y { get; set; }
}

var points = new Point[1000]; // 1000 heap allocations
```

**Correct: allocated on stack**

```csharp
// ✅ Good - allocated on stack or inline in array
public readonly struct Point
{
    public int X { get; }
    public int Y { get; }
    
    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }
}

var points = new Point[1000]; // Single allocation for array
```

**Guidelines for struct usage:**
- Size ≤ 16 bytes
- Immutable (readonly struct)
- Logically represents a single value
- Not frequently boxed

### 2.4 Avoid Boxing Value Types

**Impact: MEDIUM (prevents heap allocations)**

Boxing allocates memory and causes GC pressure. Avoid it in hot paths.

**Incorrect: boxes int**

```csharp
// ❌ Bad - boxes the integer
object value = 42; // Allocates on heap
Console.WriteLine($"Value: {value}"); // Boxing for string formatting

// Dictionary with object key
var dict = new Dictionary<object, string>();
dict[42] = "answer"; // Boxing
```

**Correct: avoids boxing**

```csharp
// ✅ Good - no boxing
int value = 42;
Console.WriteLine($"Value: {value}"); // No boxing with interpolation

// Generic dictionary
var dict = new Dictionary<int, string>();
dict[42] = "answer"; // No boxing
```

**Common boxing scenarios:**
- Casting value type to object or interface
- Using value types in non-generic collections (ArrayList, Hashtable)
- String formatting with `string.Format` (use interpolation instead)
- LINQ on value type collections without generic constraints

### 2.5 Use Object Pooling

**Impact: HIGH (reduces allocations for expensive objects)**

Pool expensive-to-create objects instead of allocating new ones.

**Implementation:**

```csharp
// ✅ Good - object pooling
public class HttpClientPool
{
    private readonly ObjectPool<HttpClient> _pool;
    
    public HttpClientPool()
    {
        var policy = new DefaultPooledObjectPolicy<HttpClient>();
        _pool = new DefaultObjectPool<HttpClient>(policy);
    }
    
    public HttpClient Rent() => _pool.Get();
    
    public void Return(HttpClient client)
    {
        // Reset state if needed
        client.DefaultRequestHeaders.Clear();
        _pool.Return(client);
    }
}

// Usage
var client = _pool.Rent();
try
{
    await client.GetAsync("https://api.example.com");
}
finally
{
    _pool.Return(client);
}
```

**Built-in object pools in ASP.NET Core:**

```csharp
services.AddSingleton<ObjectPoolProvider, DefaultObjectPoolProvider>();
services.AddSingleton(sp =>
{
    var provider = sp.GetRequiredService<ObjectPoolProvider>();
    return provider.Create(new StringBuilderPooledObjectPolicy());
});
```

### 2.6 Dispose IDisposable Properly

**Impact: HIGH (prevents resource leaks)**

Always dispose `IDisposable` objects, preferably using `using` declarations.

**Incorrect: resource leak**

```csharp
// ❌ Bad - doesn't dispose, leaks resources
public async Task<string> ReadFileAsync(string path)
{
    var reader = new StreamReader(path);
    return await reader.ReadToEndAsync();
    // StreamReader never disposed - file handle leaked
}
```

**Correct: using declaration (C# 8+)**

```csharp
// ✅ Good - automatically disposed
public async Task<string> ReadFileAsync(string path)
{
    using var reader = new StreamReader(path);
    return await reader.ReadToEndAsync();
    // Disposed at end of method
}
```

**Alternative: using statement**

```csharp
// ✅ Good - explicit scope
public async Task<string> ReadFileAsync(string path)
{
    using (var reader = new StreamReader(path))
    {
        return await reader.ReadToEndAsync();
    } // Disposed here
}
```

---

## 3. Collection Performance

**Impact: HIGH**

Collection choice and usage patterns significantly impact performance.

### 3.1 Specify Capacity for Collections

**Impact: MEDIUM (reduces resizing operations)**

Preallocate collection capacity when the size is known to avoid multiple reallocations.

**Incorrect: multiple reallocations**

```csharp
// ❌ Bad - starts at capacity 4, grows to 8, 16, 32...
var list = new List<int>();
for (int i = 0; i < 1000; i++)
{
    list.Add(i); // Reallocates 7 times
}
```

**Correct: single allocation**

```csharp
// ✅ Good - allocates once
var list = new List<int>(1000);
for (int i = 0; i < 1000; i++)
{
    list.Add(i); // No reallocations
}
```

**For StringBuilder:**

```csharp
// ❌ Bad
var sb = new StringBuilder();
for (int i = 0; i < 1000; i++)
{
    sb.Append(i);
}

// ✅ Good
var sb = new StringBuilder(10000); // Estimate capacity
for (int i = 0; i < 1000; i++)
{
    sb.Append(i);
}
```

### 3.2 Use Appropriate Collection Type

**Impact: HIGH (O(1) vs O(n) operations)**

Choose the right collection for the access pattern.

**For fast lookups:**

```csharp
// ❌ Bad - O(n) lookup
var allowedUsers = new List<string> { "alice", "bob", "charlie" };
if (allowedUsers.Contains(username)) // O(n)
{
    // ...
}

// ✅ Good - O(1) lookup
var allowedUsers = new HashSet<string> { "alice", "bob", "charlie" };
if (allowedUsers.Contains(username)) // O(1)
{
    // ...
}
```

**For key-value pairs:**

```csharp
// ❌ Bad - O(n) lookup
var users = new List<User>();
var user = users.FirstOrDefault(u => u.Id == userId); // O(n)

// ✅ Good - O(1) lookup
var users = new Dictionary<int, User>();
var user = users.GetValueOrDefault(userId); // O(1)
```

**For ordered collections:**

```csharp
// Use SortedSet<T> for ordered unique items
var sortedUsers = new SortedSet<User>(new UserComparer());

// Use SortedDictionary<K,V> for ordered key-value pairs
var sortedDict = new SortedDictionary<int, User>();
```

### 3.3 Use CollectionsMarshal for Performance

**Impact: HIGH (zero-copy access to collection internals)**

Use `CollectionsMarshal` to access internal array of List without copying.

**Incorrect: copies to array**

```csharp
// ❌ Bad - allocates new array
public void ProcessItems(List<int> items)
{
    int[] array = items.ToArray(); // Allocates and copies
    for (int i = 0; i < array.Length; i++)
    {
        Process(array[i]);
    }
}
```

**Correct: zero-copy access**

```csharp
// ✅ Good - no allocation, no copy
using System.Runtime.InteropServices;

public void ProcessItems(List<int> items)
{
    Span<int> span = CollectionsMarshal.AsSpan(items);
    for (int i = 0; i < span.Length; i++)
    {
        Process(span[i]);
    }
}
```

### 3.4 Avoid LINQ in Hot Paths

**Impact: MEDIUM (reduces allocations and overhead)**

LINQ creates enumerators and delegates, causing allocations. Use loops in performance-critical code.

**Incorrect: LINQ allocations**

```csharp
// ❌ Bad - creates enumerator and delegate
public int SumEvenNumbers(List<int> numbers)
{
    return numbers.Where(n => n % 2 == 0).Sum(); // Multiple allocations
}
```

**Correct: simple loop**

```csharp
// ✅ Good - no allocations
public int SumEvenNumbers(List<int> numbers)
{
    int sum = 0;
    for (int i = 0; i < numbers.Count; i++)
    {
        if (numbers[i] % 2 == 0)
        {
            sum += numbers[i];
        }
    }
    return sum;
}
```

**When LINQ is acceptable:**
- Not in hot path
- Readability is more important than performance
- Collection size is small (< 100 items)

---

## 4. Database & EF Core

**Impact: HIGH**

Database operations are often the bottleneck. Proper EF Core usage is critical.

### 4.1 Use AsNoTracking for Read-Only Queries

**Impact: HIGH (reduces memory and improves performance)**

Disable change tracking for queries that don't need to update entities.

**Incorrect: tracks entities unnecessarily**

```csharp
// ❌ Bad - tracks 1000 entities in memory
public async Task<List<Product>> GetProductsAsync()
{
    return await _context.Products.ToListAsync();
    // Tracks all products for changes
}
```

**Correct: no tracking**

```csharp
// ✅ Good - 30-40% faster, uses less memory
public async Task<List<Product>> GetProductsAsync()
{
    return await _context.Products
        .AsNoTracking()
        .ToListAsync();
}
```

**For all queries in a context:**

```csharp
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    }
}
```

### 4.2 Select Only Required Columns

**Impact: HIGH (reduces data transfer and memory)**

Project to DTOs instead of loading entire entities.

**Incorrect: loads all columns**

```csharp
// ❌ Bad - loads all 20+ columns
public async Task<List<Product>> GetProductNamesAsync()
{
    var products = await _context.Products.ToListAsync();
    return products.Select(p => new { p.Id, p.Name }).ToList();
    // Loads everything, then filters in memory
}
```

**Correct: loads only needed columns**

```csharp
// ✅ Good - loads only 2 columns
public async Task<List<ProductDto>> GetProductNamesAsync()
{
    return await _context.Products
        .Select(p => new ProductDto { Id = p.Id, Name = p.Name })
        .ToListAsync();
    // Database only returns Id and Name
}
```

### 4.3 Use Compiled Queries

**Impact: MEDIUM (reduces query compilation overhead)**

Compile frequently-executed queries for better performance.

**Incorrect: compiles query every time**

```csharp
// ❌ Bad - query compiled on every call
public async Task<User> GetUserAsync(int id)
{
    return await _context.Users
        .Include(u => u.Orders)
        .FirstOrDefaultAsync(u => u.Id == id);
    // EF Core compiles this every time
}
```

**Correct: compiled once**

```csharp
// ✅ Good - compiled once, reused many times
private static readonly Func<AppDbContext, int, Task<User>> _getUserQuery =
    EF.CompileAsyncQuery((AppDbContext context, int id) =>
        context.Users
            .Include(u => u.Orders)
            .FirstOrDefault(u => u.Id == id));

public async Task<User> GetUserAsync(int id)
{
    return await _getUserQuery(_context, id);
}
```

### 4.4 Use Bulk Operations

**Impact: CRITICAL (100-1000× improvement)**

Use bulk operations instead of individual inserts/updates.

**Incorrect: N database round trips**

```csharp
// ❌ Bad - 1000 individual inserts
public async Task ImportUsersAsync(List<User> users)
{
    foreach (var user in users)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync(); // 1000 round trips!
    }
}
```

**Correct: single batch**

```csharp
// ✅ Good - 1 batch operation
public async Task ImportUsersAsync(List<User> users)
{
    _context.Users.AddRange(users);
    await _context.SaveChangesAsync(); // Single batch
}
```

**For large batches, use libraries like EFCore.BulkExtensions:**

```csharp
// Best - optimized bulk insert
await _context.BulkInsertAsync(users);
```

### 4.5 Avoid N+1 Queries

**Impact: CRITICAL (N+1 queries → 1 query)**

Use eager loading or explicit loading to avoid N+1 query problems.

**Incorrect: N+1 queries**

```csharp
// ❌ Bad - 1 + N queries (1 for users, N for orders)
public async Task<List<User>> GetUsersWithOrdersAsync()
{
    var users = await _context.Users.ToListAsync(); // 1 query
    
    foreach (var user in users)
    {
        var orders = await _context.Orders
            .Where(o => o.UserId == user.Id)
            .ToListAsync(); // N queries
    }
    
    return users;
}
```

**Correct: single query with Join**

```csharp
// ✅ Good - 1 query with JOIN
public async Task<List<User>> GetUsersWithOrdersAsync()
{
    return await _context.Users
        .Include(u => u.Orders)
        .ToListAsync(); // Single query with JOIN
}
```

**For selective loading:**

```csharp
// Load related data only when needed
public async Task<List<User>> GetUsersAsync()
{
    return await _context.Users
        .Include(u => u.Orders.Where(o => o.Status == OrderStatus.Active))
        .ToListAsync();
}
```

### 4.6 Use AsSplitQuery for Large Includes

**Impact: MEDIUM (reduces data duplication)**

Use split queries to avoid cartesian explosion with multiple includes.

**Incorrect: cartesian explosion**

```csharp
// ❌ Bad - duplicates user data for each order and address
public async Task<List<User>> GetUsersAsync()
{
    return await _context.Users
        .Include(u => u.Orders)
        .Include(u => u.Addresses)
        .ToListAsync();
    // If user has 10 orders and 3 addresses, user data returned 30 times
}
```

**Correct: split into multiple queries**

```csharp
// ✅ Good - separate queries, no duplication
public async Task<List<User>> GetUsersAsync()
{
    return await _context.Users
        .Include(u => u.Orders)
        .Include(u => u.Addresses)
        .AsSplitQuery()
        .ToListAsync();
    // 3 queries: Users, Orders, Addresses
}
```

---

## 5. LINQ Optimization

**Impact: MEDIUM-HIGH**

LINQ is convenient but can be inefficient if used improperly.

### 5.1 Use Any() Instead of Count()

**Impact: MEDIUM (early termination)**

`Any()` stops at first match, `Count()` iterates entire collection.

**Incorrect: counts all items**

```csharp
// ❌ Bad - iterates entire collection
if (users.Count() > 0)
{
    // process
}

if (users.Where(u => u.IsActive).Count() > 0)
{
    // process
}
```

**Correct: stops at first item**

```csharp
// ✅ Good - stops at first item
if (users.Any())
{
    // process
}

if (users.Any(u => u.IsActive))
{
    // process
}
```

### 5.2 Use First/Single Appropriately

**Impact: LOW (better intent and error handling)**

Choose the right method based on expectations.

**Methods and when to use them:**

```csharp
// First() - expects at least one, throws if empty
var user = users.First(u => u.Id == id); // Throws if not found

// FirstOrDefault() - may be empty, returns default if not found
var user = users.FirstOrDefault(u => u.Id == id); // Returns null if not found

// Single() - expects exactly one, throws if 0 or >1
var admin = users.Single(u => u.Role == "Admin"); // Throws if 0 or multiple

// SingleOrDefault() - expects 0 or 1, throws if >1
var admin = users.SingleOrDefault(u => u.Role == "Admin"); // Returns null if not found, throws if multiple
```

**Incorrect usage:**

```csharp
// ❌ Bad - Single() when multiple are possible
var activeUser = users.Single(u => u.IsActive); // Throws if multiple active

// ❌ Bad - First() then null check
var user = users.FirstOrDefault(u => u.Id == id);
if (user == null)
    throw new NotFoundException(); // Should use First() directly
```

**Correct usage:**

```csharp
// ✅ Good - FirstOrDefault() when item may not exist
var user = users.FirstOrDefault(u => u.Id == id);
if (user == null)
    return NotFound();

// ✅ Good - First() when item must exist
var user = users.First(u => u.Id == id); // Let it throw
```

### 5.3 Avoid Multiple Enumerations

**Impact: MEDIUM (prevents redundant iterations)**

Don't enumerate `IEnumerable<T>` multiple times. Materialize to list if needed.

**Incorrect: multiple enumerations**

```csharp
// ❌ Bad - query executed 3 times
public void ProcessUsers(IEnumerable<User> users)
{
    if (users.Any()) // Execution 1
    {
        var count = users.Count(); // Execution 2
        var names = users.Select(u => u.Name).ToList(); // Execution 3
    }
}
```

**Correct: single enumeration**

```csharp
// ✅ Good - query executed once
public void ProcessUsers(IEnumerable<User> users)
{
    var userList = users.ToList(); // Execution 1
    
    if (userList.Any())
    {
        var count = userList.Count;
        var names = userList.Select(u => u.Name).ToList();
    }
}
```

**Exception:** When the source is already materialized (List, Array), no need to call ToList():

```csharp
public void ProcessUsers(List<User> users) // Already materialized
{
    if (users.Any()) // No query execution
    {
        var count = users.Count; // Property access, not method call
    }
}
```

### 5.4 Use Where Before Select

**Impact
: LOW-MEDIUM (reduces work)**

Filter before projecting to avoid unnecessary transformations.

**Incorrect: transforms all, then filters**

```csharp
// ❌ Bad - maps 10000 items, then filters to 100
var activeUserNames = users
    .Select(u => new UserDto { Id = u.Id, Name = u.Name, IsActive = u.IsActive })
    .Where(dto => dto.IsActive)
    .ToList();
```

**Correct: filters first, then transforms**

```csharp
// ✅ Good - filters to 100 items, then maps only those
var activeUserNames = users
    .Where(u => u.IsActive)
    .Select(u => new UserDto { Id = u.Id, Name = u.Name })
    .ToList();
```

---

## 6. String Operations

**Impact: MEDIUM**

String operations are common and can cause significant allocations.

### 6.1 Use StringBuilder for Concatenation

**Impact: HIGH (reduces allocations in loops)**

Use `StringBuilder` for string concatenation in loops.

**Incorrect: creates N strings**

```csharp
// ❌ Bad - creates 1000 intermediate strings
string result = "";
for (int i = 0; i < 1000; i++)
{
    result += i.ToString(); // New string allocation each iteration
}
```

**Correct: single allocation**

```csharp
// ✅ Good - single StringBuilder, minimal allocations
var sb = new StringBuilder(10000);
for (int i = 0; i < 1000; i++)
{
    sb.Append(i);
}
string result = sb.ToString();
```

### 6.2 Use String Interpolation Over Concatenation

**Impact: LOW (better performance and readability)**

String interpolation is faster and more readable than concatenation.

**Incorrect: multiple concatenations**

```csharp
// ❌ Bad - multiple string allocations
string message = "User " + user.Name + " has " + user.Orders.Count + " orders";
```

**Correct: single allocation**

```csharp
// ✅ Good - compiled to efficient code
string message = $"User {user.Name} has {user.Orders.Count} orders";
```

### 6.3 Use Span<char> for String Manipulation

**Impact: MEDIUM (zero-allocation operations)**

Use `Span<char>` for string parsing and manipulation without allocations.

**Incorrect: allocates substrings**

```csharp
// ❌ Bad - multiple string allocations
public (string, string) SplitName(string fullName)
{
    var parts = fullName.Split(' ');
    return (parts[0], parts[1]); // Allocates array and strings
}
```

**Correct: zero allocations**

```csharp
// ✅ Good - no allocations
public (ReadOnlySpan<char>, ReadOnlySpan<char>) SplitName(ReadOnlySpan<char> fullName)
{
    int index = fullName.IndexOf(' ');
    return (fullName.Slice(0, index), fullName.Slice(index + 1));
}
```

### 6.4 Use StringComparison for Comparisons

**Impact: LOW (correct behavior and performance)**

Always specify `StringComparison` to avoid culture-sensitive comparisons.

**Incorrect: culture-sensitive**

```csharp
// ❌ Bad - culture-sensitive, slower
if (fileName.EndsWith(".txt"))
{
    // May fail in Turkish locale where 'i' != 'İ'
}
```

**Correct: explicit comparison**

```csharp
// ✅ Good - explicit, faster
if (fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
{
    // Correct behavior, better performance
}
```

**Common comparisons:**
- `Ordinal` - binary comparison, fastest
- `OrdinalIgnoreCase` - case-insensitive binary comparison
- `CurrentCulture` - culture-aware comparison
- `InvariantCulture` - invariant culture comparison

---

## 7. Serialization

**Impact: MEDIUM**

Serialization is common in web APIs and can be a performance bottleneck.

### 7.1 Use System.Text.Json

**Impact: MEDIUM (better performance than Newtonsoft.Json)**

Use `System.Text.Json` instead of `Newtonsoft.Json` for better performance.

**Incorrect: slower serialization**

```csharp
// ❌ Bad - slower, more allocations
using Newtonsoft.Json;

var json = JsonConvert.SerializeObject(data);
var data = JsonConvert.DeserializeObject<User>(json);
```

**Correct: faster serialization**

```csharp
// ✅ Good - 2-3× faster, fewer allocations
using System.Text.Json;

var json = JsonSerializer.Serialize(data);
var data = JsonSerializer.Deserialize<User>(json);
```

### 7.2 Use Source Generators for JSON

**Impact: HIGH (eliminates reflection)**

Use JSON source generators to eliminate reflection overhead.

**Incorrect: uses reflection**

```csharp
// ❌ Bad - uses reflection at runtime
var json = JsonSerializer.Serialize(user);
```

**Correct: compile-time generation**

```csharp
// ✅ Good - zero reflection overhead
[JsonSerializable(typeof(User))]
[JsonSerializable(typeof(List<User>))]
internal partial class AppJsonContext : JsonSerializerContext
{
}

// Usage
var json = JsonSerializer.Serialize(user, AppJsonContext.Default.User);
var users = JsonSerializer.Deserialize<List<User>>(json, AppJsonContext.Default.ListUser);
```

**In ASP.NET Core:**

```csharp
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
    });
```

### 7.3 Configure Serialization Options

**Impact: LOW-MEDIUM (reduces payload size)**

Configure serialization to minimize payload size.

**Configuration:**

```csharp
var options = new JsonSerializerOptions
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false, // Production
    ReferenceHandler = ReferenceHandler.IgnoreCycles
};
```

---

## 8. Concurrency & Threading

**Impact: MEDIUM**

Proper use of concurrency primitives is essential for scalable applications.

### 8.1 Use SemaphoreSlim for Async Coordination

**Impact: MEDIUM (async-friendly locking)**

Use `SemaphoreSlim` instead of `lock` for async methods.

**Incorrect: cannot await inside lock**

```csharp
// ❌ Bad - cannot use await inside lock
private readonly object _lock = new object();

public async Task ProcessAsync()
{
    lock (_lock) // Cannot await here
    {
        await DoWorkAsync(); // Compiler error
    }
}
```

**Correct: async-compatible**

```csharp
// ✅ Good - can await
private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

public async Task ProcessAsync()
{
    await _semaphore.WaitAsync();
    try
    {
        await DoWorkAsync(); // OK
    }
    finally
    {
        _semaphore.Release();
    }
}
```

### 8.2 Use Concurrent Collections

**Impact: MEDIUM (thread-safe without locks)**

Use concurrent collections instead of locking regular collections.

**Incorrect: manual locking**

```csharp
// ❌ Bad - manual locking required
private readonly Dictionary<int, User> _cache = new();
private readonly object _lock = new object();

public User GetOrAdd(int id, Func<int, User> factory)
{
    lock (_lock)
    {
        if (!_cache.ContainsKey(id))
        {
            _cache[id] = factory(id);
        }
        return _cache[id];
    }
}
```

**Correct: lock-free**

```csharp
// ✅ Good - thread-safe, lock-free
private readonly ConcurrentDictionary<int, User> _cache = new();

public User GetOrAdd(int id, Func<int, User> factory)
{
    return _cache.GetOrAdd(id, factory);
}
```

### 8.3 Use Channels for Producer-Consumer

**Impact: MEDIUM (efficient async queuing)**

Use `System.Threading.Channels` for producer-consumer scenarios.

**Implementation:**

```csharp
// ✅ Good - efficient async producer-consumer
public class MessageProcessor
{
    private readonly Channel<Message> _channel;
    
    public MessageProcessor()
    {
        _channel = Channel.CreateUnbounded<Message>();
        _ = ProcessMessagesAsync();
    }
    
    public async ValueTask EnqueueAsync(Message message)
    {
        await _channel.Writer.WriteAsync(message);
    }
    
    private async Task ProcessMessagesAsync()
    {
        await foreach (var message in _channel.Reader.ReadAllAsync())
        {
            await ProcessMessageAsync(message);
        }
    }
}
```

---

## 9. Advanced Patterns

**Impact: LOW-MEDIUM**

Advanced patterns for specific optimization scenarios.

### 9.1 Use Lazy<T> for Expensive Initialization

**Impact: MEDIUM (defers expensive work)**

Use `Lazy<T>` to defer expensive initialization until first use.

**Incorrect: always initializes**

```csharp
// ❌ Bad - always computes, even if never used
public class ConfigurationService
{
    private readonly Dictionary<string, string> _config;
    
    public ConfigurationService()
    {
        _config = LoadConfigurationFromFile(); // Always runs
    }
}
```

**Correct: initializes on first use**

```csharp
// ✅ Good - only loads when accessed
public class ConfigurationService
{
    private readonly Lazy<Dictionary<string, string>> _config;
    
    public ConfigurationService()
    {
        _config = new Lazy<Dictionary<string, string>>(LoadConfigurationFromFile);
    }
    
    public string Get(string key) => _config.Value[key];
}
```

### 9.2 Use ValueTask for Frequently Synchronous Paths

**Impact: MEDIUM (reduces allocations)**

Return `ValueTask` from interface methods that may complete synchronously.

**Example: cache that often hits**

```csharp
// ✅ Good - no allocation on cache hit
public interface ICache
{
    ValueTask<User> GetUserAsync(int id);
}

public class MemoryCache : ICache
{
    private readonly Dictionary<int, User> _cache = new();
    
    public ValueTask<User> GetUserAsync(int id)
    {
        if (_cache.TryGetValue(id, out var user))
        {
            return new ValueTask<User>(user); // No allocation
        }
        
        return new ValueTask<User>(LoadFromDatabaseAsync(id));
    }
}
```

### 9.3 Use ref struct for Stack-Only Types

**Impact: LOW (ensures stack allocation)**

Use `ref struct` for types that must stay on the stack.

**Example:**

```csharp
// ✅ Good - guaranteed stack allocation
public ref struct StackOnlyBuffer
{
    private Span<byte> _buffer;
    
    public StackOnlyBuffer(Span<byte> buffer)
    {
        _buffer = buffer;
    }
    
    public void Process()
    {
        // Work with buffer
    }
}

// Usage
Span<byte> buffer = stackalloc byte[256];
var processor = new StackOnlyBuffer(buffer);
processor.Process();
```

### 9.4 Use Static Lambdas

**Impact: LOW (avoids closure allocations)**

Use static lambdas when they don't capture variables.

**Incorrect: captures this**

```csharp
// ❌ Bad - captures 'this', allocates closure
public void ProcessItems(List<int> items)
{
    items.ForEach(item => Process(item)); // Captures 'this'
}
```

**Correct: no capture**

```csharp
// ✅ Good - no closure allocation
public void ProcessItems(List<int> items, Processor processor)
{
    items.ForEach(static item => Processor.Process(item)); // No capture
}
```

### 9.5 Use Records for DTOs

**Impact: LOW (less boilerplate, value semantics)**

Use records for data transfer objects.

**Incorrect: manual implementation**

```csharp
// ❌ Bad - lots of boilerplate
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    
    public override bool Equals(object obj) { /* ... */ }
    public override int GetHashCode() { /* ... */ }
}
```

**Correct: concise and correct**

```csharp
// ✅ Good - automatic value semantics
public record UserDto(int Id, string Name);

// With validation
public record UserDto(int Id, string Name)
{
    public UserDto(int Id, string Name) : this(Id, Name)
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Name is required");
    }
}
```

---

## Summary Checklist

### Critical (Must Do)
- ✅ Never use `async void` except for event handlers
- ✅ Use `Task.WhenAll` for parallel operations
- ✅ Never block on async with `.Result` or `.Wait()`
- ✅ Use `AsNoTracking()` for read-only EF queries
- ✅ Avoid N+1 queries - use `Include()` or split queries
- ✅ Use bulk operations for batch database updates
- ✅ Dispose `IDisposable` objects properly

### High Priority
- ✅ Use `ArrayPool<T>` for temporary buffers
- ✅ Use `Span<T>` and `Memory<T>` for zero-copy operations
- ✅ Specify collection capacity when size is known
- ✅ Use appropriate collection types (HashSet, Dictionary)
- ✅ Use `StringBuilder` for string concatenation in loops
- ✅ Use `ConfigureAwait(false)` in library code

### Medium Priority
- ✅ Use `ValueTask<T>` for frequently synchronous methods
- ✅ Use compiled queries for repeated EF queries
- ✅ Project to DTOs in EF queries (select only needed columns)
- ✅ Use `System.Text.Json` over Newtonsoft.Json
- ✅ Use `SemaphoreSlim` for async coordination
- ✅ Use `Any()` instead of `Count() > 0`

### Low Priority (Optimizations)
- ✅ Use object pooling for expensive objects
- ✅ Use source generators for JSON serialization
- ✅ Use `Lazy<T>` for expensive initialization
- ✅ Use records for DTOs
- ✅ Use static lambdas when possible

---

## References

1. [Microsoft .NET Performance](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-8#performance)
2. [EF Core Performance](https://learn.microsoft.com/en-us/ef/core/performance/)
3. [System.Text.Json](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview)
4. [Span<T> and Memory<T>](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/)
5. [Async/Await Best Practices](https://learn.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)