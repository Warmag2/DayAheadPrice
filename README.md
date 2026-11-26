# DayAheadPrice

## Usage

First, have a linux server with docker installed.

Then:

Copy `infra/.env_sample` to `infra/.env` and edit it to suit your needs.
You need an API key from ENTSO-E to use the API. They have a half-automated system for just asking for one, and home automation is a sufficient reason.

Then, edit src/DayAheadPrice/appsettings.json:
* PricingOptions contains options for modifying the base price. Set the margin and VAT so that they are correct as per your country and electricity seller. The final price is `market price × (1 + Vat) + Margin`: give `Margin` in c/kWh **including VAT** (as Finnish sellers usually quote it), and `Vat` as a fraction (0.255 for 25.5%). The `Alert*Threshold` values (final c/kWh) set when the day summary flags a high average, a high hourly average and a high peak price.
* EndpointOptions contains options related to the ENTSO-E endpoint used. BaseURL is the URL of the API, and the Domain contains the string for the pricing domain that electricity prices are asked from. The defaults present apply for Finland.

Finally, in the root of the repository, type:

    ./refresh.sh

Done.

NOTE: This service is unsecured and the default configuration does not expect to use any certificates.
      You can fix this by altering the appsettings.conf and adding a Kestrel block which requires
      certificates, but I recommend using a virtual host and reverse proxy in NGINX or Apache.
      The app trusts the proxy's X-Forwarded-* headers, so keep the container port published on 127.0.0.1 only.
      Apache does not send X-Forwarded-Proto by default; add `RequestHeader set X-Forwarded-Proto "https"`
      to the HTTPS virtual host so the app generates https links. Blazor's live connection should use WebSockets;
      on Apache 2.4.47+ enable mod_proxy_http's upgrade (otherwise SignalR falls back to long polling):

          ProxyPass / http://127.0.0.1:8548/ upgrade=websocket
          ProxyPassReverse / http://127.0.0.1:8548/
