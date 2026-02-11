#!/bin/sh
cd infra
docker build --tag dayaheadprice -f ../src/DayAheadPrice/Dockerfile ..

docker compose down
docker compose up -d
cd ..
