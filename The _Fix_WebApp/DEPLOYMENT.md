# Deploying FashionFix to Oracle Cloud (Ampere A1) with your domain

This covers your exact setup: **Ampere A1 (ARM64)** free-tier instance, domain
DNS **not yet pointed** at it. Because Ampere A1 is ARM, and Microsoft doesn't
ship SQL Server for ARM at all (not even in Docker), this project's database
was switched from SQL Server to **PostgreSQL**, which runs natively on ARM.
That change is already done in this codebase (`FashionFix.Web.csproj`,
`Program.cs`, `appsettings.json`).

Stack: Ubuntu 22.04 (ARM64) → .NET 8 → PostgreSQL → Nginx (reverse proxy +
TLS) → your domain.

---

## 0. Before you start

- Create the Ampere A1 instance in OCI if you haven't: **Ubuntu 22.04**,
  shape `VM.Standard.A1.Flex` (1–4 OCPU / 6–24GB is all within the free
  allowance). Note its **public IP**.
- Make sure you can SSH in: `ssh ubuntu@<public-ip>` using the key pair OCI
  gave you at creation.
- Know your domain registrar login (wherever you bought the domain).

---

## 1. Point your domain at the instance

At your domain registrar's DNS settings, add:

| Type | Name | Value            |
|------|------|------------------|
| A    | @    | `<public-ip>`    |
| A    | www  | `<public-ip>`    |

DNS propagation can take a few minutes to a few hours. You can check with:
```bash
dig +short yourdomain.com
```
Don't move on to the Let's Encrypt step (step 10) until this returns your
instance's IP — Let's Encrypt verifies ownership by reaching your domain over
HTTP.

---

## 2. Open the ports (the step people usually miss on OCI)

Two separate firewalls both need opening — missing either one means "site
doesn't load" with no obvious error.

**a) OCI Security List / Network Security Group** (in the OCI Console):
Networking → Virtual Cloud Networks → your VCN → your subnet → Security
Lists → Default Security List → Add Ingress Rules:
- Source `0.0.0.0/0`, IP Protocol TCP, Destination Port `80`
- Source `0.0.0.0/0`, IP Protocol TCP, Destination Port `443`

**b) The instance's own firewall** (Ubuntu images on OCI ship with iptables
rules active by default, on top of the cloud firewall):
```bash
sudo iptables -I INPUT -p tcp --dport 80 -j ACCEPT
sudo iptables -I INPUT -p tcp --dport 443 -j ACCEPT
sudo netfilter-persistent save
```

---

## 3. System update + install .NET 8 SDK

We'll build directly on the server — it has plenty of CPU/RAM, and it avoids
any cross-compilation headaches from building on a different architecture.

```bash
sudo apt update && sudo apt upgrade -y

# Microsoft's package feed for Ubuntu 22.04 arm64
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

sudo apt update
sudo apt install -y dotnet-sdk-8.0

dotnet --version   # should print 8.x
```

---

## 4. Install PostgreSQL and create the database

```bash
sudo apt install -y postgresql postgresql-contrib
sudo systemctl enable --now postgresql

sudo -u postgres psql
```
Inside the `psql` prompt:
```sql
CREATE DATABASE fashionfixdb;
CREATE USER fashionfix_app WITH ENCRYPTED PASSWORD 'CHOOSE_A_STRONG_PASSWORD';
GRANT ALL PRIVILEGES ON DATABASE fashionfixdb TO fashionfix_app;
\c fashionfixdb
GRANT ALL ON SCHEMA public TO fashionfix_app;
\q
```

Update the connection string to match — either edit `appsettings.json`
directly, or (better, so the password isn't sitting in a committed file)
set it as an environment variable in the systemd service in step 8:
```
Host=localhost;Port=5432;Database=fashionfixdb;Username=fashionfix_app;Password=CHOOSE_A_STRONG_PASSWORD
```

---

## 5. Get the code onto the server

Easiest if you push this project to a GitHub repo (private is fine) first,
then on the server:
```bash
sudo mkdir -p /var/www
cd /var/www
sudo git clone https://github.com/yourusername/FashionFix.git fashionfix-src
cd fashionfix-src
```
No GitHub yet? `scp` the folder up instead:
```bash
# run this on your own machine, not the server
scp -r ./FashionFix.Web ubuntu@<public-ip>:~/fashionfix-src
```

---

## 6. Restore, build the database schema, and publish

From inside the project folder on the server:
```bash
cd /var/www/fashionfix-src   # or ~/fashionfix-src if you scp'd it
dotnet restore

# Create the first migration (the project has entities but no migration yet)
dotnet tool install --global dotnet-ef
export PATH="$PATH:$HOME/.dotnet/tools"
dotnet ef migrations add InitialCreate
dotnet ef database update

# Publish a release build
dotnet publish -c Release -o /tmp/fashionfix-publish
sudo mkdir -p /var/www/fashionfix
sudo cp -r /tmp/fashionfix-publish/* /var/www/fashionfix/
sudo chown -R www-data:www-data /var/www/fashionfix
```

---

## 7. Run it once by hand to sanity-check

```bash
cd /var/www/fashionfix
sudo -u www-data ASPNETCORE_URLS=http://localhost:5000 dotnet FashionFix.Web.dll
```
In another terminal on the server: `curl http://localhost:5000` should
return HTML. Ctrl+C to stop it — next we'll let systemd manage it instead.

---

## 8. Set it up as a systemd service (auto-start, auto-restart)

The file is already prepared at `deploy/fashionfix.service` in this project.
```bash
sudo cp /var/www/fashionfix-src/deploy/fashionfix.service /etc/systemd/system/fashionfix.service
sudo nano /etc/systemd/system/fashionfix.service
# uncomment/fill in the ConnectionStrings__DefaultConnection line with your real password

sudo systemctl daemon-reload
sudo systemctl enable --now fashionfix
sudo systemctl status fashionfix   # should show "active (running)"
```

---

## 9. Install Nginx and reverse-proxy your domain to the app

```bash
sudo apt install -y nginx

sudo cp /var/www/fashionfix-src/deploy/nginx-fashionfix.conf /etc/nginx/sites-available/fashionfix
sudo nano /etc/nginx/sites-available/fashionfix
# replace yourdomain.com / www.yourdomain.com with your actual domain

sudo ln -s /etc/nginx/sites-available/fashionfix /etc/nginx/sites-enabled/
sudo rm -f /etc/nginx/sites-enabled/default
sudo nginx -t          # should say "syntax is ok" / "test is successful"
sudo systemctl reload nginx
```

At this point `http://yourdomain.com` should load the app (once DNS from
step 1 has propagated).

---

## 10. HTTPS with Let's Encrypt (free, auto-renewing)

Only do this once `dig +short yourdomain.com` returns your server's IP.
```bash
sudo apt install -y certbot python3-certbot-nginx
sudo certbot --nginx -d yourdomain.com -d www.yourdomain.com
```
Follow the prompts (enter an email, agree to terms, choose "redirect HTTP to
HTTPS" when asked). Certbot edits the Nginx config for you and sets up
auto-renewal via a systemd timer — nothing further to do.

---

## 11. Verify

- `https://yourdomain.com` loads the FashionFix login page over HTTPS.
- `sudo systemctl status fashionfix` → active.
- `sudo systemctl status nginx` → active.
- Logs if something's off: `journalctl -u fashionfix -f`

---

## Redeploying after code changes

```bash
cd /var/www/fashionfix-src
git pull                      # or re-scp your changes
dotnet ef database update     # only if you added new migrations
dotnet publish -c Release -o /tmp/fashionfix-publish
sudo systemctl stop fashionfix
sudo cp -r /tmp/fashionfix-publish/* /var/www/fashionfix/
sudo systemctl start fashionfix
```

---

## Common gotchas

- **"Connection refused" from outside, but `curl localhost:5000` works on
  the server**: almost always the OCI Security List (step 2a) or the
  instance's iptables (step 2b) — not the app.
- **Certbot fails domain validation**: DNS hasn't propagated yet, or port 80
  isn't actually reachable from the internet (same two firewalls again).
- **App works over `http://<ip>:5000` but not the domain**: Nginx isn't
  proxying — check `server_name` in the Nginx config matches your domain
  exactly, and `sudo nginx -t` for syntax errors.
- **500 error on first load**: usually the database — check the connection
  string password matches what you set in PostgreSQL, and that migrations
  were applied (`dotnet ef database update`).
