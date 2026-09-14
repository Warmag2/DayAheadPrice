#!/bin/sh
cd infra
docker build --tag dayaheadpricesdk -f ./sdk/Dockerfile ..
docker build --tag dayaheadprice -f ../src/DayAheadPrice/Dockerfile ..

docker-compose down
docker-compose up -d
cd ..
