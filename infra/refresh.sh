#!/bin/sh
docker build --tag dayaheadprice -f ../src/DayAheadPrice/Dockerfile ..

docker-compose down
docker-compose up -d
