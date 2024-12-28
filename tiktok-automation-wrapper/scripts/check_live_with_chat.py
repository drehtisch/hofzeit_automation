import asyncio
import requests
import datetime
import logging

from TikTokLive import TikTokLiveClient
from TikTokLive.client.logger import LogLevel
from TikTokLive.events import ConnectEvent, DisconnectEvent, LiveEndEvent, ControlEvent, CommentEvent
from TikTokLive.proto import ControlAction

client: TikTokLiveClient = TikTokLiveClient(
    unique_id="@mindsetxtherapy"
)

def setup_logging(log_level):
    """
    Setzt die Logging-Konfiguration basierend auf dem gewählten Level.
    """
    logging.basicConfig(
        level=log_level,
        format="%(asctime)s - %(levelname)s - %(message)s",
        handlers=[logging.FileHandler("tiktok_live_checker.log"), logging.StreamHandler()]
    )

@client.on(ConnectEvent)
async def on_connect(event: ConnectEvent):
    client.logger.info(f"Connected to @{event.unique_id}!")
    
@client.on(DisconnectEvent)
async def on_disconnect(event: DisconnectEvent):
    client.logger.info("Disconnected.")
    
@client.on(LiveEndEvent)
async def on_liveend(event: LiveEndEvent):
    client.logger.info(f"Live Ended {ControlAction(event.action).name}")

@client.on(ControlEvent)
async def on_control(event: ControlEvent):
    client.logger.info(f"Control event {ControlAction(event.action).name}")
    
@client.on(CommentEvent)
async def on_comment(event: CommentEvent):
    client.logger.info(f"[{datetime.datetime.now()}] {event.user.unique_id} | {event.comment}")

async def check_loop():
    # Run 24/7
    run = True
    while run:
        try:
            # Check if they're live
            while not await client.is_live():
                client.logger.info("Client is currently not live. Checking again in 60 seconds.")
                await asyncio.sleep(60)  # Spamming the endpoint will get you blocked

            # Connect once they become live
            client.logger.info("Requested client is live!")
            
            await client.connect()
        except KeyboardInterrupt:
            client.disconnect()
            raise KeyboardInterrupt
        except Exception as e:
            client.logger.error(f"An unexpected error occured: {e}")
        


if __name__ == '__main__':
    client.logger.setLevel(LogLevel.INFO.value)
    loop = asyncio.get_event_loop()
    loop.run_until_complete(check_loop())
