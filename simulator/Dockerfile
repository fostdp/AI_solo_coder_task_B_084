
FROM python:3.11-slim

WORKDIR /app

COPY simulator/requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

COPY simulator/ ./

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD ["python", "-c", "print('healthy')"]

ENTRYPOINT ["python", "zigbee.py"]
CMD ["--interval", "14400", "--dust-sensors", "30", "--fungi-sensors", "20"]
