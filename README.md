# DayAheadPrice

## Usage

First, have a linux server with docker installed.

Then:

Copy `infra/.env_sample` to `infra/.env` and edit it to suit your need.

Finally, type:

    cd infra/
    ./refresh.sh

Done.

NOTE: This service is unsecured and the default configuration does not expect to use any certificates.
      You can fix this by altering the appsettings.conf and adding a Kestrel block which requires
      certificates, but I recommend using a virtual host and reverse proxy in NGINX or Apache
