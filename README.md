# DayAheadPrice

## Usage

First, have a linux server with docker installed.

Then:

Copy `infra/.env_sample` to `infra/.env` and edit it to suit your needs.
You need an API key from ENTSO-E to use the API. They have a half-automated system for just asking for one, and home automation is a sufficient reason.

Then, edit src/DayAheadPrice/appsettings.json:
* PricingOptions contains options for modifying the base price. Set the margin and VAT so that they are correct as per your country and electricity seller.
* EndpointOptions contains options related to the ENTSO-E endpoint used. BaseURL is the URL of the API, and the Domain contains the string for the pricing domain that electricity prices are asked from. The defaults present apply for Finland.

Finally, in the root of the repository, type:

    ./refresh.sh

Done.

NOTE: This service is unsecured and the default configuration does not expect to use any certificates.
      You can fix this by altering the appsettings.conf and adding a Kestrel block which requires
      certificates, but I recommend using a virtual host and reverse proxy in NGINX or Apache.
