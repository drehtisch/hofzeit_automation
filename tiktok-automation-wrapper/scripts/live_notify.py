import asyncio
import datetime
import sys
import argparse
from TikTokLive import TikTokLiveClient
from TikTokLive.client.logger import LogLevel
from TikTokLive.events import ConnectEvent, DisconnectEvent, LiveEndEvent, ControlEvent, CommentEvent
from TikTokLive.proto import ControlAction

client: TikTokLiveClient = None
stop_event = asyncio.Event()

async def handle_liveend():
    try:
        print("~LIVE_ENDED")
        if client.connected:
            await client.disconnect()
    except Exception as e:
        print(f"An unexpected error handling LiveEnd occured: {e}")

async def on_connect(event: ConnectEvent):
    print(f"Connected to @{event.unique_id}!")
    
async def on_disconnect(event: DisconnectEvent):
    print("~DISCONNECTED")
    
async def on_liveend(event: LiveEndEvent):
    await handle_liveend()

async def on_control(event: ControlEvent):
    print(f"Control event {ControlAction(event.action).name}")
    if event.action == 3 or event.action == 4:
        await handle_liveend()

async def on_comment(event: CommentEvent):
    print(f"[{datetime.datetime.now()}] {event.user.unique_id} | {event.comment}")


async def check_loop():
    print("~STARTED")
    was_live = False
    while not stop_event.is_set():
        try:
            is_live = await client.is_live()
            
            if is_live:
                if not was_live:
                    print("~LIVE")
                    if not client.connected:
                        await client.connect()
                was_live = True    
            else:
                if was_live:
                    await handle_liveend()
                was_live = False
        except Exception as e:
            print(f"An unexpected error occured: {e}")
        finally:
            await asyncio.sleep(60)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument("-n", "--name", help="TikTok Username", default="hofzeitprojekt")
    args = parser.parse_args()
    
    client = TikTokLiveClient(unique_id=args.name)
    client.logger.setLevel(LogLevel.INFO.value)
    client.logger.info(f"Running check for user @{client.unique_id}")
    
    client.add_listener(ControlEvent, on_control)
    client.add_listener(LiveEndEvent, on_liveend)
    client.add_listener(DisconnectEvent, on_disconnect)
    client.add_listener(ConnectEvent, on_connect)
    client.add_listener(CommentEvent, on_comment)
    
    try:
        asyncio.run(check_loop())
    except KeyboardInterrupt:
        print("Shutting down gracefully")
        stop_event.set()
        asyncio.run(client.disconnect())
