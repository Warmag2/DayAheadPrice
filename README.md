# DayAheadPrice

## Usage

First, have a linux server with docker installed.

Then:

Copy `infra/refresh.sh.sample` to `infra/refresh.sh` and edit it to point to your SSH certificates.\
Copy `infra/.env_sample` to `infra/.env` and edit it to suit your need.

Finally, type:

    cd infra/
    ./refresh.sh

Done.
