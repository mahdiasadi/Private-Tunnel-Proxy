# Private Tunnel Proxy V3

یک **HTTP/HTTPS و SOCKS5 Proxy خصوصی مبتنی بر .NET** که بدون نیاز به VPS، از یک ASP.NET Core Web App به‌عنوان Remote Tunnel Server استفاده می‌کند.

این پروژه برای سناریوهایی طراحی شده که کاربر یک هاست ASP.NET Core در اختیار دارد و می‌خواهد ترافیک برنامه دسکتاپ خود را از طریق آن هاست به اینترنت منتقل کند.

> ⚠️ این پروژه برای استفاده خصوصی و کنترل‌شده طراحی شده است. آن را بدون احراز هویت و محدودیت دسترسی به یک Open Proxy عمومی تبدیل نکنید.

بابت پیشنهادات و مشکلات  :mahdiasadi@yahoo.com,sahagroup@gmail.com

## ✨ ویژگی‌ها

* HTTP Proxy
* HTTPS Proxy با `CONNECT`
* SOCKS5 Proxy
* اجرای Local Proxy روی:

  * `127.0.0.1:8888`
* Remote Tunnel روی ASP.NET Core
* بدون نیاز به VPS
* بدون نیاز به نصب VPN
* پشتیبانی از HTTP و HTTPS
* احراز هویت با Secret Key
* محدودسازی مقصد به پورت‌های `80` و `443`
* جلوگیری از اتصال مستقیم به Private IPها
* پشتیبانی از چند Session
* مدیریت Timeout و Cleanup
* TCP tunneling
* مناسب برای Windows Desktop
* قابل استفاده با Chrome، Edge، curl و سایر برنامه‌هایی که Proxy را پشتیبانی می‌کنند

---

# 🏗 Architecture

```
                  Internet
                     ▲
                     │
              Target Web Server
                     ▲
                     │ TCP
                     │
        ┌──────────────────────────┐
        │ ASP.NET Core Web App     │
        │ Remote Tunnel Server     │
        │                          │
        │ Free ASP Hosting        │
        └────────────┬─────────────┘
                     │
                     │ HTTPS
                     │
        ┌────────────▼─────────────┐
        │ Desktop Local Proxy      │
        │                          │
        │ HTTP / HTTPS / SOCKS5    │
        │ 127.0.0.1:8888           │
        └────────────┬─────────────┘
                     │
                     ▲
                     │
              Browser / App
```

---

# 🔄 نحوه کار

فرض کنیم مرورگر درخواست زیر را ارسال کند:

```
https://httpbin.org/ip
```

مرورگر به Local Proxy متصل می‌شود:

```
127.0.0.1:8888
```

Local Proxy درخواست `CONNECT` را دریافت می‌کند:

```
CONNECT httpbin.org:443
```

سپس Local Proxy از Remote Server درخواست ایجاد Tunnel می‌کند:

```
POST /tunnel/create
```

همراه با:

```
X-Target-Host: httpbin.org
X-Target-Port: 443
X-Proxy-Key: ********
```

Remote Server یک TCP Connection به مقصد ایجاد می‌کند.

سپس:

```
Browser
   ↓
Local Proxy
   ↓
HTTPS Tunnel
   ↓
ASP.NET Core Server
   ↓
TCP
   ↓
httpbin.org
```

داده‌های برگشتی نیز مسیر معکوس را طی می‌کنند.

---

# 🧰 Technologies

## .NET

هسته پروژه با **C# و .NET** نوشته شده است.

تکنولوژی‌های اصلی:

* C#
* .NET 8+
* ASP.NET Core
* Minimal API
* `TcpListener`
* `TcpClient`
* `NetworkStream`
* `HttpClient`
* `CancellationToken`
* `ConcurrentDictionary`
* Async/Await

---

## ASP.NET Core Minimal API

Remote Server با Minimal API پیاده‌سازی شده است.

Endpointهای اصلی:

```
GET  /
GET  /health

POST /tunnel/create

POST /tunnel/{id}/send

GET  /tunnel/{id}/receive

POST /tunnel/{id}/close
```

Minimal API باعث می‌شود Server کوچک و سبک باقی بماند و به MVC یا Controllerهای اضافی نیاز نداشته باشد.

---

# 🌐 Proxy Protocols

## HTTP Proxy

برای درخواست‌های HTTP:

```
curl -x http://127.0.0.1:8888 http://example.com
```

---

## HTTPS Proxy

برای HTTPS از استاندارد HTTP `CONNECT` استفاده می‌شود:

```
CONNECT example.com:443
```

پس از دریافت:

```
HTTP/1.1 200 Connection Established
```

TLS بین Client و Target برقرار می‌شود.

Remote Server محتوای TLS را Decode نمی‌کند.

بنابراین:

```
Browser
   │
   │ TLS
   ▼
Target Server
```

و Tunnel فقط بایت‌ها را منتقل می‌کند.

---

## SOCKS5

پروژه یک SOCKS5 Listener نیز ارائه می‌کند:

```
127.0.0.1:8888
```

مثال:

```bash
curl --proxy socks5h://127.0.0.1:8888 https://httpbin.org/ip
```

استفاده از `socks5h` باعث می‌شود DNS نیز از مسیر SOCKS عبور کند.

---

# 🔐 Security

این پروژه عمداً چند محدودیت امنیتی دارد.

## Proxy Key

هر درخواست Remote باید دارای Secret باشد:

```
X-Proxy-Key
```

مثال:

```csharp
const string ProxyKey =
    "YOUR-LONG-RANDOM-SECRET";
```

همان Secret باید روی Client و Server تنظیم شود.

---

## Port Restriction

برای جلوگیری از سوءاستفاده، مقصد فقط به پورت‌های:

```
80
443
```

محدود شده است.

---

## Private IP Protection

Server نباید اجازه دهد Proxy به مواردی مانند:

```
127.0.0.1
10.0.0.0/8
172.16.0.0/12
192.168.0.0/16
169.254.0.0/16
```

متصل شود.

این موضوع برای جلوگیری از SSRF و دسترسی ناخواسته به شبکه داخلی اهمیت دارد.

---

# 🚀 Installation

## Server

پروژه ASP.NET Core را روی هاست خود Deploy کنید.

بعد از Deploy ابتدا:

```
https://YOUR-DOMAIN/health
```

را باز کنید.

باید چیزی شبیه این دریافت کنید:

```json
{
  "success": true,
  "sessions": 0
}
```

---

## Desktop

پروژه Console را اجرا کنید.

باید ببینید:

```
Private Tunnel Proxy V3

HTTP  : 127.0.0.1:8888
HTTPS : 127.0.0.1:8888
SOCKS5: 127.0.0.1:8888

Proxy is ready.
```

---

# 🧪 Testing

## HTTP

```bash
curl -x http://127.0.0.1:8888 http://httpbin.org/ip
```

## HTTPS

```bash
curl -v -x http://127.0.0.1:8888 https://httpbin.org/ip
```

## SOCKS5

```bash
curl -v --proxy socks5h://127.0.0.1:8888 https://httpbin.org/ip
```

در صورت موفقیت باید IP مربوط به Remote Server را مشاهده کنید.

---

# 🌍 Browser

می‌توان Proxy ویندوز را روی:

```
Address: 127.0.0.1
Port: 8888
```

قرار داد.

Chrome و Edge نیز از Proxy سیستم استفاده می‌کنند.

سپس:

```
https://httpbin.org/ip
```

را باز کنید.

---

# ⚠️ محدودیت‌ها

این پروژه جایگزین کامل VPS یا VPN نیست.

مخصوصاً روی Free ASP.NET Hosting ممکن است محدودیت‌هایی مانند موارد زیر وجود داشته باشد:

* محدودیت CPU
* محدودیت RAM
* محدودیت تعداد Connection
* محدودیت زمان اجرای Request
* محدودیت Background Task
* محدودیت Idle Timeout
* Restart شدن Application
* محدودیت Traffic
* محدودیت Concurrent Connections

به همین دلیل عملکرد آن به نوع Hosting وابسته است.

---

# ⚡ Performance

برای انتقال داده از:

```
NetworkStream
```

استفاده شده است.

Buffer پیش‌فرض:

```
32 KB
```

در نظر گرفته شده است.

ارتباط‌ها به‌صورت Async پردازش می‌شوند تا Threadهای اضافی مصرف نشوند.

Sessionها با:

```csharp
ConcurrentDictionary
```

مدیریت می‌شوند.

---

# 📁 Project Structure

پیشنهاد ساختار Repository:

```
PrivateTunnelProxy/
│
├── Server/
│   ├── Program.cs
│   └── Server.csproj
│
├── Desktop/
│   ├── Program.cs
│   └── Desktop.csproj
│
├── README.md
├── README.fa.md
├── LICENSE
└── .gitignore
```

---

# 🔑 Configuration

قبل از انتشار عمومی GitHub، Secret واقعی خود را از Source Code حذف کنید.

بهتر است از Environment Variable استفاده شود:

```
PROXY_KEY
```

و Secret واقعی هرگز در Git Commit قرار نگیرد.

---

# 📌 Use Cases

این پروژه می‌تواند برای موارد زیر استفاده شود:

* دسترسی Proxy خصوصی
* تست شبکه
* تست برنامه‌های Desktop
* تست HTTP/HTTPS Client
* تست SOCKS5
* Route کردن ترافیک یک Application
* آزمایش Tunnelهای مبتنی بر HTTP
* محیط‌های Development و Testing

---

# 🛡 Responsible Use

این پروژه برای استفاده قانونی و مجاز طراحی شده است.

از آن برای:

* دسترسی غیرمجاز
* دور زدن کنترل‌های امنیتی
* حمله به سرویس‌ها
* Open Proxy عمومی
* سوءاستفاده از منابع Hosting

استفاده نکنید.

---



# ⭐ Roadmap

نسخه‌های آینده می‌توانند شامل موارد زیر باشند:

* Persistent Tunnel
* HTTP/2 Streaming
* Connection Pooling
* Compression
* Traffic Statistics
* Authentication بهتر
* مدیریت چند Server
* Failover
* Health Check
* Auto Reconnect
* Proxy Rotation
* Desktop Tray Application
* Windows Service
* Configuration File
* Logging
* Bandwidth Limiting
* Per-user Authentication
* TLS Certificate Pinning
* Admin Dashboard

-------------------------------------------------------------------------------------------
# Private-Tunnel-Proxy

A lightweight **private HTTP/HTTPS and SOCKS5 proxy tunnel built with .NET and ASP.NET Core**, designed to work without requiring a VPS.

The project uses an ASP.NET Core web application as a remote tunnel server and a lightweight .NET desktop application as the local proxy.

It is especially useful when you have access to ASP.NET hosting but do not have a VPS.

> ⚠️ This project is intended for private and controlled use. Do not deploy it as an unauthenticated public open proxy.

For contact me :mahdiasadi@yahoo.com,sahagroup@gmail.com
---

## ✨ Features

* HTTP Proxy
* HTTPS Proxy using `CONNECT`
* SOCKS5 Proxy
* Local proxy on:

  * `127.0.0.1:8888`
* Remote tunnel using ASP.NET Core
* No VPS required
* No VPN installation required
* HTTP and HTTPS support
* Secret-key authentication
* Destination port restrictions
* Private IP protection
* Multiple concurrent sessions
* Session timeout and cleanup
* TCP tunneling
* Windows desktop support
* Works with browsers, curl and applications supporting HTTP/SOCKS proxies

---

# 🏗 Architecture


                  Internet
                     ▲
                     │
              Target Web Server
                     ▲
                     │ TCP
                     │
        ┌──────────────────────────┐
        │ ASP.NET Core Web App     │
        │ Remote Tunnel Server     │
        │                          │
        │ ASP.NET Hosting          │
        └────────────┬─────────────┘
                     │
                     │ HTTPS
                     │
        ┌────────────▼─────────────┐
        │ Desktop Local Proxy      │
        │                          │
        │ HTTP / HTTPS / SOCKS5    │
        │ 127.0.0.1:8888           │
        └────────────┬─────────────┘
                     │
                     ▲
                     │
              Browser / App
```

---

# 🔄 How It Works

Suppose a browser requests:

```
https://httpbin.org/ip
```

The browser connects to the local proxy:

```
127.0.0.1:8888
```

For HTTPS, the browser sends:

```
CONNECT httpbin.org:443
```

The local proxy then asks the remote ASP.NET Core server to create a tunnel:

```
POST /tunnel/create
```

with:

```
X-Target-Host: httpbin.org
X-Target-Port: 443
X-Proxy-Key: ********
```

The remote server creates a TCP connection to the target.

The resulting flow is:

```
Browser
   ↓
Local Proxy
   ↓
HTTPS Tunnel
   ↓
ASP.NET Core Server
   ↓
TCP
   ↓
Target Server
```

Response data travels back through the same tunnel.

---

# 🧰 Technologies

## .NET

The project is written in **C# and .NET**.

Main technologies include:

* C#
* .NET 8+
* ASP.NET Core
* Minimal API
* `TcpListener`
* `TcpClient`
* `NetworkStream`
* `HttpClient`
* `CancellationToken`
* `ConcurrentDictionary`
* Async/Await

---

# ASP.NET Core Minimal API

The remote server uses ASP.NET Core Minimal APIs.

Main endpoints:

```
GET  /
GET  /health

POST /tunnel/create

POST /tunnel/{id}/send

GET  /tunnel/{id}/receive

POST /tunnel/{id}/close
```

Minimal APIs keep the server lightweight without requiring MVC controllers.

---

# 🌐 Proxy Protocols

## HTTP Proxy

Example:

```bash
curl -x http://127.0.0.1:8888 http://example.com
```

---

## HTTPS Proxy

HTTPS connections use the standard HTTP `CONNECT` mechanism:

```
CONNECT example.com:443
```

After:

```
HTTP/1.1 200 Connection Established
```

the TLS connection is established between the client and the target server.

The remote tunnel server does not decrypt HTTPS traffic.

The tunnel simply forwards bytes:

```
Browser
   │
   │ TLS
   ▼
Target Server
```

---

# SOCKS5

The local application also exposes a SOCKS5 endpoint:

```
127.0.0.1:8888
```

Example:

```bash
curl --proxy socks5h://127.0.0.1:8888 https://httpbin.org/ip
```

Using `socks5h` allows hostname resolution to happen through the SOCKS proxy.

---

# 🔐 Security

The project includes several security restrictions.

## Proxy Authentication

Remote requests must include:

```
X-Proxy-Key
```

Example:

```csharp
const string ProxyKey =
    "YOUR-LONG-RANDOM-SECRET";
```

The same secret must be configured on both client and server.

---

## Destination Port Restriction

The server currently allows only:

```
80
443
```

This reduces the risk of the tunnel being abused to access arbitrary services.

---

## Private IP Protection

The server rejects private/local IPv4 addresses such as:

```
127.0.0.1
10.0.0.0/8
172.16.0.0/12
192.168.0.0/16
169.254.0.0/16
```

This helps reduce SSRF and unintended access to internal networks.

---

# 🚀 Installation

## Server

Deploy the ASP.NET Core application to your hosting provider.

After deployment, open:

```
https://YOUR-DOMAIN/health
```

Expected response:

```json
{
  "success": true,
  "sessions": 0
}
```

---

## Desktop

Run the local proxy application.

You should see:

```
Private Tunnel Proxy V3

HTTP  : 127.0.0.1:8888
HTTPS : 127.0.0.1:8888
SOCKS5: 127.0.0.1:8888

Proxy is ready.
```

---

# 🧪 Testing

## HTTP

```bash
curl -x http://127.0.0.1:8888 http://httpbin.org/ip
```

## HTTPS

```bash
curl -v -x http://127.0.0.1:8888 https://httpbin.org/ip
```

## SOCKS5

```bash
curl -v --proxy socks5h://127.0.0.1:8888 https://httpbin.org/ip
```

If everything is working correctly, the returned IP should be the IP address of the remote hosting server.

---

# 🌍 Browser

Configure the operating system proxy:

```
Address: 127.0.0.1
Port: 8888
```

Chrome and Edge can use the system proxy configuration.

Then open:

```
https://httpbin.org/ip
```

The reported IP should correspond to the remote server.

---

# ⚠️ Limitations

This project is **not a full replacement for a VPS or VPN**.

Performance and reliability depend heavily on the hosting provider.

Free ASP.NET hosting may impose limitations such as:

* CPU limits
* Memory limits
* Connection limits
* Request execution limits
* Background task restrictions
* Idle timeouts
* Application restarts
* Traffic limits
* Concurrent connection limits

Therefore, this project should be considered a lightweight tunnel rather than a production-grade VPN infrastructure.

---

# ⚡ Performance

The tunnel uses:

```
NetworkStream
```

for TCP data transfer.

The default buffer size is:

```
32 KB
```

Network operations are asynchronous to avoid unnecessary thread usage.

Active sessions are managed using:

```csharp
ConcurrentDictionary
```

---

# 📁 Project Structure

Recommended repository structure:

```
PrivateTunnelProxy/
│
├── Server/
│   ├── Program.cs
│   └── Server.csproj
│
├── Desktop/
│   ├── Program.cs
│   └── Desktop.csproj
│
├── README.md
├── README.fa.md
├── LICENSE
└── .gitignore
```

---

# 🔑 Configuration

Do not publish real secrets to GitHub.

Instead of hardcoding:

```csharp
const string ProxyKey =
    "real-secret";
```

prefer using an environment variable:

```
PROXY_KEY
```

The real secret should never be committed to Git.

---

# 📌 Use Cases

Possible use cases include:

* Private proxy access
* Network testing
* Desktop application testing
* HTTP/HTTPS client testing
* SOCKS5 testing
* Application traffic routing
* HTTP tunnel experiments
* Development environments
* Testing proxy-aware applications

---

# 🛡 Responsible Use

This project is intended for legal and authorized use.

Do not use it for:

* Unauthorized access
* Attacking services
* Operating a public open proxy
* Abusing hosting resources
* Bypassing security controls without authorization

---



# ⭐ Roadmap

Potential future improvements:

* Persistent tunnels
* HTTP/2 streaming
* Connection pooling
* Compression
* Traffic statistics
* Stronger authentication
* Multi-server support
* Failover
* Health checks
* Automatic reconnect
* Desktop tray application
* Windows Service
* Configuration files
* Structured logging
* Bandwidth limiting
* Per-user authentication


--------------------------------------------------------------------------------------------------------------

* TLS certificate pinning
* Administration dashboard
