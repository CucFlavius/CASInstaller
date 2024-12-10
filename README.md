!Still in development

- The application attempts to download and install a game from the blizzard cdn (or your own), without using agent
- Currently only tested with wow_classic_era because that was small enough for debugging purposes

## Command-Line Arguments

The following command-line arguments are supported by this application:

- `-p`, `--product`: Specifies the product to be installed. Eg. "wow"
- `-b`, `--branch`: Specifies the branch of the product to be installed. Default: "us"
- `-i`, `--install-path`: Specifies the installation path for the product. Default "", Eg. "D:/Games"
- `--override-cdn-config`: Override the 16byte hash to use for downloading the CDNConfig.
- `--override-build-config`: Override 16byte hash to use for downloading the BuildConfig.
- `--override-hosts`: Override the Hosts in CDN, here you would type space separated hosts, for use with custom CDN. Eg. "localhost:1000"
