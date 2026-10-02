import os
import sys
from rich import print as rprint
from dotenv import load_dotenv

from auth import FacebookAuthManager
from page import FacebookPageManager

load_dotenv()

def main():
    user_token = os.getenv("FB_SHORT_LIVED_USER_TOKEN")
    if not user_token:
        rprint("[bold red]Error:[/] FB_SHORT_LIVED_USER_TOKEN environment variable is missing.")
        sys.exit(1)

    rprint("[bold green]=== Facebook Graph API Manager ===[/]")
    
    auth_mgr = FacebookAuthManager(user_token)
    rprint("\n[bold yellow]1. Fetching Managed Facebook Pages...[/]")
    pages = auth_mgr.get_user_pages()

    if not pages:
        rprint("[bold red]No pages found for this user access token.[/]")
        return

    for idx, page in enumerate(pages, 1):
        rprint(f" [{idx}] Page Name: [bold]{page['name']}[/] | ID: {page['id']}")

    selected_page = pages[0]
    page_id = selected_page["id"]
    page_token = selected_page["access_token"]

    page_mgr = FacebookPageManager(page_id=page_id, page_access_token=page_token)

    rprint(f"\n[bold yellow]2. Fetching info for '{selected_page['name']}'...[/]")
    info = page_mgr.get_page_info()
    rprint(info)

    rprint("\n[bold yellow]3. Publishing Test Post...[/]")
    post_res = page_mgr.publish_text_post(
        message="?? Hello World from our Python Graph API App!"
    )
    rprint(f"[bold green]Post published successfully! ID:[/] {post_res.get('id')}")

if __name__ == "__main__":
    main()
