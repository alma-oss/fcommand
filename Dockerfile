FROM dcreg.service.consul/dev/development-dotnet-core-sdk-common:3.1

# build scripts
COPY ./fake.sh /library/
COPY ./build.fsx /library/
COPY ./paket.dependencies /library/
COPY ./paket.references /library/
COPY ./paket.lock /library/

# sources
COPY ./Command.fsproj /library/
COPY ./src /library/src

# copy tests
COPY ./tests /library/tests

# others
COPY ./.git /library/.git
COPY ./CHANGELOG.md /library/

WORKDIR /library

RUN \
    ./fake.sh build target Build no-clean

CMD ["./fake.sh", "build", "target", "Tests", "no-clean"]
